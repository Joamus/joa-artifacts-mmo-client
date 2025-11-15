using Application.ArtifactsApi.Schemas;
using Application.ArtifactsApi.Schemas.Responses;
using Application.Character;
using Application.Dtos;
using Application.Errors;
using Application.Services;
using Applicaton.Jobs;
using Applicaton.Services.FightSimulator;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public class ObtainItem : CharacterJob
{
    public bool AllowUsingMaterialsFromBank { get; set; } = true;

    public bool AllowUsingMaterialsFromInventory { get; set; } = true;

    public bool CanTriggerTraining { get; set; } = true;

    private List<DropSchema> itemsInBank { get; set; } = [];

    protected int _progressAmount { get; set; } = 0;

    public ObtainItem(PlayerCharacter playerCharacter, GameState gameState, string code, int amount)
        : base(playerCharacter, gameState)
    {
        Code = code;
        Amount = amount;
    }

    public void ForCharacter(PlayerCharacter recipient)
    {
        if (recipient.Schema.Name == Character.Schema.Name)
        {
            // it's a-me, no reason to deposit etc.
            return;
        }

        onSuccessEndHook = () =>
        {
            logger.LogInformation(
                $"{JobName}: [{Character.Schema.Name}] onSuccessEndHook: for character {recipient.Schema.Name} - queueing job to deposit {Amount} x {Code} to the bank"
            );

            var depositItemJob = new DepositItems(
                Character,
                gameState,
                Code,
                Amount
            ).SetParent<DepositItems>(this);

            depositItemJob.onSuccessEndHook = () =>
            {
                logger.LogInformation(
                    $"{JobName}: [{Character.Schema.Name}] onSuccessEndHook: for character {recipient.Schema.Name} - queueing job to withdraw {Amount} x {Code} from the bank"
                );
                recipient.RemoveFromWishlist(Code, Amount);

                recipient.QueueJob(
                    new WithdrawItem(recipient, gameState, Code, Amount, false),
                    true
                );
                return Task.Run(() => { });
            };

            Character.QueueJob(depositItemJob, true);

            return Task.Run(() => { });
        };
    }

    public void ForBank()
    {
        onSuccessEndHook = () =>
        {
            logger.LogInformation(
                $"{JobName}: [{Character.Schema.Name}] onSuccessEndHook: queueing job to deposit {Amount} x {Code} to the bank"
            );

            var depositItemJob = new DepositItems(Character, gameState, Code, Amount);
            Character.QueueJob(depositItemJob, true);

            return Task.Run(() => { });
        };
    }

    protected override async Task<OneOf<AppError, None>> ExecuteAsync()
    {
        // It's not very elegant that this job is pasted in multiple places, but a lot of jobs want to have their inventory be clean before they start, or in their InnerJob.
        if (DepositUnneededItems.ShouldInitDepositItems(Character, true))
        {
            Character.QueueJobsBefore(Id, [new DepositUnneededItems(Character, gameState)]);
            Status = JobStatus.Suspend;
            return new None();
        }

        List<CharacterJob> jobs = [];
        logger.LogInformation(
            $"{JobName}: [{Character.Schema.Name}] run started - progress {Code} ({_progressAmount}/{Amount})"
        );

        var bankResult = await gameState.BankItemCache.GetBankItems(Character);

        if (bankResult is not BankItemsResponse bankItemsResponse)
        {
            return new AppError("Failed to get bank items");
        }

        itemsInBank = bankItemsResponse.Data;
        // }
        // useItemIfInInventory is set to the job's value at first, so we can allow obtaining an item we already have.
        // But if we have the ingredients in our inventory, then we should always use them (for now).
        // Having this variable will allow us to e.g craft multiple copper daggers, else we could only have 1 in our inventory

        var result = await GetJobsRequired(
            Character,
            gameState,
            AllowUsingMaterialsFromBank,
            itemsInBank,
            jobs,
            Code,
            Amount,
            AllowUsingMaterialsFromInventory,
            CanTriggerTraining
        );

        logger.LogInformation(
            $"{JobName}: [{Character.Schema.Name}] found {jobs.Count} jobs to run, to obtain item {Code}"
        );

        switch (result.Value)
        {
            case AppError jobError:
                return jobError;
        }

        /**
        * The onSuccessEndHook is a bit funky for ObtainItems, because actually don't want it to run when the ObtainItem job ends,
        * because the job doesn't do anything, but just queues other jobs. So we want it to run, when the last job in the list is done,
        * usually when the last item is crafted
        */

        if (jobs.Count > 0)
        {
            jobs.Last()!.onSuccessEndHook = onSuccessEndHook;

            foreach (var job in jobs)
            {
                job.SetParent<CharacterJob>(this);
            }

            Character.QueueJobsAfter(Id, jobs);
        }

        // Reset it
        onSuccessEndHook = null;

        return new None();
    }

    /**
     * Get all the jobs required to obtain an item
     * We mutate a list to recursively add all the required jobs to the list
    */
    public static async Task<OneOf<AppError, None>> GetJobsRequired(
        PlayerCharacter Character,
        GameState gameState,
        bool allowUsingItemFromBank,
        List<DropSchema> itemsInBank,
        List<CharacterJob> jobs,
        string code,
        int amount,
        bool allowUsingItemFromInventory = false,
        bool canTriggerTraining = false,
        bool firstIteration = true
    )
    {
        var matchingItem = gameState.Items.Find(item => item.Code == code);

        if (matchingItem is null)
        {
            return new AppError($"Could not find item with code {code} - could not gather it");
        }

        // We have the item already, no need to get it again

        int amountInInventory =
            !firstIteration && allowUsingItemFromInventory
                ? (Character.GetItemFromInventory(code)?.Quantity ?? 0)
                : 0;

        if (!firstIteration && allowUsingItemFromInventory && amountInInventory >= amount)
        {
            return new None();
        }

        if (!firstIteration && allowUsingItemFromBank)
        {
            var matchingItemInBank = itemsInBank.FirstOrDefault(item => item.Code == code);
            int amountInBank = matchingItemInBank?.Quantity ?? 0;

            int amountToTakeFromBank = Math.Min(amountInBank, amount);

            if (amountToTakeFromBank > 0)
            {
                jobs.Add(new WithdrawItem(Character, gameState, code, amountToTakeFromBank));

                amount -= amountToTakeFromBank;
            }
        }

        int requiredAmount = amount - amountInInventory;

        if (requiredAmount <= 0)
        {
            return new None();
        }

        if (matchingItem.Craft is not null)
        {
            // if the total ingredients of the items is higher than 60

            int totalIngredientsForCrafting = 0;

            foreach (var item in matchingItem.Craft.Items)
            {
                totalIngredientsForCrafting += item.Quantity * requiredAmount;
            }

            /*
                We want to split the crafting, if we need a lot of ingredients, e.g. if we need 80 copper ore,
                then it's safer to split it in 2, if we our character's max inventory space is only 100
            */

            List<int> iterations = CalculateObtainItemIterations(
                matchingItem,
                Character,
                requiredAmount
            );

            foreach (var iterationAmount in iterations)
            {
                foreach (var item in matchingItem.Craft.Items)
                {
                    int itemAmount = item.Quantity * iterationAmount;

                    var result = await GetJobsRequired(
                        Character,
                        gameState,
                        allowUsingItemFromBank,
                        itemsInBank,
                        jobs,
                        item.Code,
                        itemAmount,
                        true,
                        canTriggerTraining,
                        false
                    );

                    switch (result.Value)
                    {
                        case AppError jobError:
                            return jobError;
                    }
                }
                var craftItemJob = new CraftItem(Character, gameState, code, iterationAmount);
                craftItemJob.CanTriggerTraining = canTriggerTraining;

                jobs.Add(craftItemJob);
            }

            return new None();
        }
        List<ResourceSchema> resources = gameState.Resources.FindAll(resource =>
            resource.Drops.Find(drop => drop.Code == code && drop.Rate > 0) != null
        );

        if (resources.Count > 0)
        {
            var gatherJob = new GatherResourceItem(Character, gameState, code, requiredAmount);
            gatherJob.CanTriggerTraining = canTriggerTraining;

            jobs.Add(gatherJob);
            return new None();
        }

        if (matchingItem.Subtype == "task")
        {
            if (matchingItem.Code == ItemService.TasksCoin)
            {
                return new AppError(
                    $"Cannot handle that we need to obtain \"tasks_coins\"",
                    ErrorStatus.InsufficientSkill
                );
            }
            // Pick up a task, or complete one you have
            if (Character.Schema.TaskType == "monsters")
            {
                var monster = gameState.Monsters.Find(monster =>
                    monster.Drops.Find(drop => drop.Code == code) is not null
                );
                if (monster is null)
                {
                    return new AppError(
                        $"Monster with code {code} not found",
                        ErrorStatus.NotFound
                    );
                }

                if (
                    FightSimulator
                        .CalculateFightOutcome(Character.Schema, monster, gameState)
                        .ShouldFight
                )
                {
                    jobs.Add(new MonsterTask(Character, gameState, matchingItem.Code, amount));
                    return new None();
                }
                return new AppError(
                    $"You cannot obtain item with code {code}, because you need to complete your monster task, and you cannot beat the monster",
                    ErrorStatus.InsufficientSkill
                );
            }
            else
            {
                jobs.Add(
                    new DoTaskUntilObtainedItem(
                        Character,
                        gameState,
                        TaskType.items,
                        matchingItem.Code,
                        amount
                    )
                );
            }
        }
        else if (matchingItem.Subtype == "npc")
        {
            var matchingNpcItem = gameState.NpcItemsDict.GetValueOrNull(matchingItem.Code);

            if (matchingNpcItem is null)
            {
                // something is wrong
                return new AppError(
                    $"The item with code {code} is an NPC item, but cannot find it in NPC items list",
                    ErrorStatus.NotFound
                );
            }

            if (matchingNpcItem.BuyPrice is null)
            {
                return new AppError(
                    $"The item with code {code} is an NPC item, but the buyPrice is null - currency is {matchingNpcItem.Currency}",
                    ErrorStatus.NotFound
                );
            }

            jobs.Add(
                new ObtainOrFindItem(
                    Character,
                    gameState,
                    matchingNpcItem.Currency,
                    (int)matchingNpcItem.BuyPrice * requiredAmount
                )
            );

            jobs.Add(
                new BuyItemNpc(
                    Character,
                    gameState,
                    matchingItem.Code,
                    requiredAmount,
                    true,
                    true,
                    true
                )
            );

            /**
             * Look in our inventory, and see if we have the required gold/items
             * If yes, then buy the item
             * If no, look in our bank
             * If yes, go to the bank, withdraw, and then buy
             * If no, return error here - we cannot get it - or even better:
             * We queue a job for obtaining the item needed, and then after that queue a buy job for the item.
             * Maybe a buy job can have a parameter, which allows obtaining the mats?
             * Remember gold is a material in this case, but handle it specially. Can be withdrawn from bank, else just
             * grind gold from the most suitable monster (closest to level that we can beat)
            */
        }
        else
        {
            List<MonsterSchema> suitableMonsters = [];

            var monstersThatDropTheItem = gameState.Monsters.FindAll(monster =>
                monster.Drops.Find(drop => drop.Code == code) is not null
            );

            if (monstersThatDropTheItem is null)
            {
                return new AppError(
                    $"The item with code {code} is unobtainable",
                    ErrorStatus.NotFound
                );
            }

            monstersThatDropTheItem.Sort(
                (b, a) =>
                {
                    int aDropRate = a.Drops.Find(drop => drop.Code == code)!.Rate;
                    int bDropRate = b.Drops.Find(drop => drop.Code == code)!.Rate;

                    // The lower the number, the higher the drop rate, so we want to sort them like this;
                    return aDropRate.CompareTo(bDropRate);
                }
            );

            MonsterSchema? lowestLevelMonster = null;

            foreach (var monster in monstersThatDropTheItem)
            {
                if (
                    FightSimulator
                        .CalculateFightOutcomeWithBestEquipment(Character, monster, gameState)
                        .ShouldFight
                )
                {
                    var job = new FightMonster(
                        Character,
                        gameState,
                        monster.Code,
                        requiredAmount,
                        code
                    );

                    // job.AllowUsingMaterialsFromInventory = true;
                    jobs.Add(job);

                    return new None();
                }
                else
                {
                    if (lowestLevelMonster is null || monster.Level < lowestLevelMonster.Level)
                    {
                        lowestLevelMonster = monster;
                    }
                }
            }

            if (canTriggerTraining)
            {
                // TODO: Trigger levelling up the character until they can?
                // return jobs.Add(new Train)
            }

            return new AppError(
                $"Cannot fight any monsters that drop item {code} - {Character.Schema.Name} would lose",
                ErrorStatus.InsufficientSkill
            );
        }

        return new None();
    }

    // TODO: Make the iterations into something like "craftPerIteration", so it returns a list of tuples or something,
    // e.g if you have to create 10 iron bars, it might be, 3, 3, 3, 1 or something
    public static List<int> CalculateObtainItemIterations(
        ItemSchema item,
        PlayerCharacter character,
        int totalItemsWantedAmount
    )
    {
        int totalInventorySpaceNeeded;

        int spaceNeededPerItem;

        if (item.Craft is not null)
        {
            int materialsNeeded = 0;

            foreach (var material in item.Craft.Items)
            {
                materialsNeeded += material.Quantity;
            }

            spaceNeededPerItem = materialsNeeded;

            totalInventorySpaceNeeded = materialsNeeded * totalItemsWantedAmount;
        }
        else
        {
            spaceNeededPerItem = 1;
            totalInventorySpaceNeeded = totalItemsWantedAmount;
        }

        int totalItemsWantedAmountRemaining = totalItemsWantedAmount;

        List<int> iterations = [];

        // Adding leeway with - 10. We use max items, because we assume that the character will deposit stuff
        int availableInventorySpace = character.GetInventorySpaceLeft() - 5;

        int iterationAmount = (int)
            Math.Ceiling((double)totalInventorySpaceNeeded / availableInventorySpace);

        for (int i = 0; i < iterationAmount; i++)
        {
            int amountObtainedInIteration = Math.Min(
                (int)Math.Floor((double)availableInventorySpace / spaceNeededPerItem),
                totalItemsWantedAmountRemaining
            );

            totalItemsWantedAmountRemaining -= amountObtainedInIteration;

            // Should always be true
            if (amountObtainedInIteration > 0)
            {
                iterations.Add(amountObtainedInIteration);
            }
        }

        return iterations;
    }
}
