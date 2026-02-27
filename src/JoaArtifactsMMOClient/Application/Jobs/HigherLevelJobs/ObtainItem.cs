using Application.ArtifactsApi.Schemas;
using Application.ArtifactsApi.Schemas.Responses;
using Application.Character;
using Application.Dtos;
using Application.Errors;
using Application.Records;
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
            recipient.RemoveFromWishlist(Code, Amount);
            // it's a-me, no reason to deposit etc.
            return;
        }

        onSuccessEndHook = async () =>
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

            depositItemJob.onSuccessEndHook = async () =>
            {
                logger.LogInformation(
                    $"{JobName}: [{Character.Schema.Name}] onSuccessEndHook: for character {recipient.Schema.Name} - queueing job to withdraw {Amount} x {Code} from the bank"
                );
                recipient.RemoveFromWishlist(Code, Amount);

                await recipient.QueueJob(
                    new WithdrawItem(recipient, gameState, Code, Amount, false),
                    true
                );
            };

            await Character.QueueJob(depositItemJob, true);
        };
    }

    public void ForBank()
    {
        onSuccessEndHook = async () =>
        {
            logger.LogInformation(
                $"{JobName}: [{Character.Schema.Name}] onSuccessEndHook: queueing job to deposit {Amount} x {Code} to the bank"
            );

            var depositItemJob = new DepositItems(Character, gameState, Code, Amount);
            await Character.QueueJob(depositItemJob, true);
        };
    }

    protected override async Task<OneOf<AppError, None>> ExecuteAsync()
    {
        // // It's not very elegant that this job is pasted in multiple places, but a lot of jobs want to have their inventory be clean before they start, or in their InnerJob.
        if (DepositUnneededItems.ShouldInitDepositItems(Character, true))
        {
            await Character.QueueJobsBefore(
                Id,
                [new DepositUnneededItems(Character, gameState, null, true)]
            );
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

        switch (result.Value)
        {
            case AppError jobError:
                return jobError;
        }
        logger.LogInformation(
            $"{JobName}: [{Character.Schema.Name}] found {jobs.Count} jobs to run, to obtain item {Code}"
        );

        /**
        * The onSuccessEndHook is a bit funky for ObtainItems, because actually don't want it to run when the ObtainItem job ends,
        * because the job doesn't do anything, but just queues other jobs. So we want it to run, when the last job in the list is done,
        * usually when the last item is crafted
        */

        if (jobs.Count > 0)
        {
            jobs.Last().onSuccessEndHook = onSuccessEndHook;
            jobs.Last().onAfterSuccessEndHook = onAfterSuccessEndHook;

            foreach (var job in jobs)
            {
                job.SetParent<CharacterJob>(this);
            }

            await Character.QueueJobsAfter(Id, jobs);
        }

        // Reset it
        onSuccessEndHook = null;
        onAfterSuccessEndHook = null;

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
        bool canTriggerTraining = false
    )
    {
        var result = await InnerGetJobsRequired(
            Character,
            Character
                .Schema.Inventory.Select(item => new DropSchema
                {
                    Code = item.Code,
                    Quantity = item.Quantity,
                })
                .ToList(),
            gameState,
            allowUsingItemFromBank,
            itemsInBank
                .Select(item => new DropSchema { Code = item.Code, Quantity = item.Quantity })
                .ToList(),
            jobs,
            code,
            amount,
            allowUsingItemFromInventory,
            canTriggerTraining,
            true
        );

        return result;
    }

    /**
     * Get all the jobs required to obtain an item
     * We mutate a list to recursively add all the required jobs to the list
    */
    static async Task<OneOf<AppError, None>> InnerGetJobsRequired(
        PlayerCharacter Character,
        List<DropSchema> itemsInInventory,
        GameState gameState,
        bool allowUsingItemFromBank,
        List<DropSchema> itemsInBankClone,
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

        DropSchema? itemFromInventory =
            !firstIteration && allowUsingItemFromInventory
                ? itemsInInventory.FirstOrDefault(item => item.Code == code)
                : null;

        if (!firstIteration && allowUsingItemFromInventory && itemFromInventory?.Quantity >= amount)
        {
            return new None();
        }

        if (!firstIteration && allowUsingItemFromBank)
        {
            var matchingItemInBank = itemsInBankClone.FirstOrDefault(item => item.Code == code);
            int amountInBank = matchingItemInBank?.Quantity ?? 0;

            int amountToTakeFromBank = Math.Min(amountInBank, amount);

            if (amountToTakeFromBank > 0)
            {
                jobs.Add(new WithdrawItem(Character, gameState, code, amountToTakeFromBank, false));
                matchingItemInBank!.Quantity -= amountToTakeFromBank;

                amount -= amountToTakeFromBank;
            }
        }

        int requiredAmount = amount - (itemFromInventory?.Quantity ?? 0);

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

            if (iterations.Count == 0)
            {
                jobs.Add(new DepositUnneededItems(Character, gameState, null, true));
                return new None();
            }

            foreach (var iterationAmount in iterations)
            {
                foreach (var item in matchingItem.Craft.Items)
                {
                    int itemAmount = item.Quantity * iterationAmount;

                    var result = await InnerGetJobsRequired(
                        Character,
                        itemsInInventory,
                        gameState,
                        allowUsingItemFromBank,
                        itemsInBankClone,
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
            bool allResourcesAreFromEvents = true;

            foreach (var resource in resources)
            {
                var resourceIsFromEvent = gameState.EventService.IsEntityFromEvent(resource.Code);

                if (
                    resourceIsFromEvent
                    && gameState.EventService.WhereIsEntityActive(resource.Code) is null
                )
                {
                    continue;
                }
                else
                {
                    allResourcesAreFromEvents = false;
                    break;
                }
            }

            if (allResourcesAreFromEvents)
            {
                return new AppError(
                    $"Cannot gather item \"{code}\" - it is from an event, but the event is not active",
                    ErrorStatus.InsufficientSkill
                );
            }
            var gatherJob = new GatherResourceItem(Character, gameState, code, requiredAmount);
            gatherJob.CanTriggerTraining = canTriggerTraining;

            jobs.Add(gatherJob);
            return new None();
        }

        var matchingNpcItem = gameState.NpcItemsDict.GetValueOrNull(matchingItem.Code);

        if (matchingItem.Code == ItemService.TasksCoin)
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

            return new None();
        }

        if (matchingItem.Subtype == "task")
        {
            // BuyPrice should not be null here - this is how you obtain task items.
            int taskCoinsNeeded = (matchingNpcItem!.BuyPrice ?? 0) * requiredAmount;
            int taskCoinsNeededFromInventory = taskCoinsNeeded;

            var taskCoinsInInventory = itemsInInventory.FirstOrDefault(item =>
                item.Code == ItemService.TasksCoin
            );

            var taskCoinsInBank = itemsInBankClone.FirstOrDefault(item =>
                item.Code == ItemService.TasksCoin
            );

            taskCoinsNeeded -= Math.Min(taskCoinsInInventory?.Quantity ?? 0, taskCoinsNeeded);

            // For now we only care if the bank has all we need - else the CompleteTask job will withdraw needed coins
            if (
                (taskCoinsInInventory?.Quantity ?? 0) < taskCoinsNeeded
                && (taskCoinsInBank?.Quantity ?? 0) >= taskCoinsNeeded
            )
            {
                int amountToWithdraw = Math.Min(taskCoinsNeeded, taskCoinsInBank?.Quantity ?? 0);

                jobs.Add(
                    new WithdrawItem(
                        Character,
                        gameState,
                        ItemService.TasksCoin,
                        amountToWithdraw,
                        true
                    )
                );

                taskCoinsInBank!.Quantity -= amountToWithdraw;
                taskCoinsNeeded = 0;
                taskCoinsNeededFromInventory -= amountToWithdraw;
            }
            if (
                taskCoinsInInventory is not null
                && taskCoinsInInventory.Quantity >= taskCoinsNeededFromInventory
            )
            {
                taskCoinsInInventory.Quantity -= taskCoinsNeededFromInventory;
            }

            if (taskCoinsNeeded == 0)
            {
                jobs.Add(
                    new BuyItemNpc(Character, gameState, code, requiredAmount, true, true, true)
                );
                return new None();
            }

            // Pick up a task, or complete one you have
            if (Character.Schema.TaskType == "monsters")
            {
                var monster = gameState.AvailableMonstersDict.GetValueOrDefault(
                    Character.Schema.Task
                );

                if (monster is null)
                {
                    return new AppError(
                        $"Monster with code {code} was not found",
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
            else if (
                string.IsNullOrEmpty(Character.Schema.Task)
                || await Character.PlayerActionService.CanItemFromItemTaskBeObtained()
            )
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
            else
            {
                return new AppError(
                    $"You cannot obtain item with code {code}, because the current item task cannot be completed, since it requires items from an event that is not active",
                    ErrorStatus.InsufficientSkill
                );
            }
            return new None();
        }

        List<MonsterSchema> suitableMonsters = [];

        var monstersThatDropTheItem = gameState.AvailableMonsters.FindAll(monster =>
            monster.Drops.Find(drop => drop.Code == code) is not null
        );

        if (monstersThatDropTheItem is null)
        {
            return new AppError($"The item with code {code} is unobtainable", ErrorStatus.NotFound);
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

        var foundMonsterThatIsFromEvent = false;

        monstersThatDropTheItem = await GetDefeatableMonstersFromList(
            Character,
            gameState,
            monstersThatDropTheItem,
            itemsInBankClone
        );

        if (monstersThatDropTheItem.Count > 0)
        {
            monstersThatDropTheItem.Sort((a, b) => a.Level - b.Level);

            lowestLevelMonster = monstersThatDropTheItem.ElementAt(0);
        }

        if (lowestLevelMonster is not null)
        {
            List<CharacterJob> withdrawItemJobs =
                await FightMonster.GetWithdrawItemJobsIfBetterItemsInBank(
                    Character,
                    gameState,
                    lowestLevelMonster
                );
            var fightSimIfUsingWithdrawnItems =
                FightSimulator.FindBestFightEquipmentWithUsablePotions(
                    Character,
                    gameState,
                    lowestLevelMonster,
                    itemsInBankClone
                        .Select(item => new ItemInInventory
                        {
                            Item = gameState.ItemsDict[item.Code],
                            Quantity = item.Quantity,
                        })
                        .ToList()
                );

            if (
                fightSimIfUsingWithdrawnItems is null
                || !fightSimIfUsingWithdrawnItems.Outcome.ShouldFight
            )
            {
                return new AppError(
                    $"Cannot fight {lowestLevelMonster.Code} to obtain item with code {code}"
                );
            }

            // Don't really care if the sim uses the withdrawn items or not, we can fight them
            if (fightSimIfUsingWithdrawnItems.Outcome.ShouldFight)
            {
                var job = new FightMonster(
                    Character,
                    gameState,
                    lowestLevelMonster.Code,
                    requiredAmount,
                    code
                );

                // job.AllowUsingMaterialsFromInventory = true;
                jobs.Add(job);

                return new None();
            }
        }

        if (monstersThatDropTheItem.Count > 0)
        {
            if (foundMonsterThatIsFromEvent)
            {
                return new AppError(
                    $"The monster that drops {code} is likely from an event, but the event is not active - {Character.Schema.Name} cannot obtain {code}",
                    ErrorStatus.InsufficientSkill
                );
            }
            else
            {
                return new AppError(
                    $"Cannot fight any monsters that drop item {code} - {Character.Schema.Name} would lose",
                    ErrorStatus.InsufficientSkill
                );
            }
        }

        if (matchingNpcItem is not null)
        {
            if (matchingNpcItem.BuyPrice is null)
            {
                return new AppError(
                    $"The item with code {code} is an NPC item, but the buyPrice is null - currency is {matchingNpcItem.Currency}",
                    ErrorStatus.NotFound
                );
            }

            var npcIsFromEvent = gameState.EventService.IsEntityFromEvent(matchingNpcItem.Npc);

            if (
                npcIsFromEvent
                && gameState.EventService.WhereIsEntityActive(matchingNpcItem.Npc) is null
            )
            {
                return new AppError(
                    $"Cannot buy from NPC \"{matchingNpcItem.Npc}\" - it is from an event, but the event is not active",
                    ErrorStatus.InsufficientSkill
                );
            }

            int amountOfCurrency =
                matchingNpcItem.Currency == "gold"
                    ? Character.Schema.Gold
                    : Character.GetItemFromInventory(matchingNpcItem.Currency)?.Quantity ?? 0;

            if (matchingNpcItem.Currency == "gold" && matchingNpcItem.BuyPrice > amountOfCurrency)
            {
                return new AppError(
                    $"Matching item costs more gold than the character currently has - cannot obtain",
                    ErrorStatus.Undefined
                );
            }

            int neededCurrency = (int)matchingNpcItem.BuyPrice * requiredAmount;

            if (amountOfCurrency < neededCurrency)
            {
                bool canObtainCurrencyFromMonsters = true;

                if (matchingNpcItem.Currency != "gold")
                {
                    var monstersThatDropCurrency = gameState.AvailableMonsters.FindAll(monster =>
                        monster.Drops.Exists(drop => drop.Code == matchingNpcItem.Currency)
                    );

                    if (monstersThatDropCurrency.Count == 0)
                    {
                        return new AppError(
                            $"Currency \"{matchingNpcItem.Currency}\" cannot be obtained - there are no monsters that drop the currency",
                            ErrorStatus.Undefined
                        );
                    }

                    monstersThatDropCurrency = await GetDefeatableMonstersFromList(
                        Character,
                        gameState,
                        monstersThatDropCurrency,
                        itemsInBankClone
                    );

                    if (monstersThatDropCurrency.Count == 0)
                    {
                        canObtainCurrencyFromMonsters = false;
                    }
                }

                if (!canObtainCurrencyFromMonsters)
                {
                    return new AppError(
                        $"Currency \"{matchingNpcItem.Currency}\" cannot be obtained - there are monsters that drop it, but they are either not defeatable or from events which are not active",
                        ErrorStatus.Undefined
                    );
                }

                jobs.Add(
                    new ObtainOrFindItem(
                        Character,
                        gameState,
                        matchingNpcItem.Currency,
                        neededCurrency - amountOfCurrency
                    )
                );
            }

            var npcIsAccessible = gameState.AvailableNpcs.Exists(npc =>
                npc.Code == matchingNpcItem.Code
            );

            if (!npcIsAccessible)
            {
                return new AppError(
                    $"NPC \"{matchingNpcItem.Code}\" is not accessible",
                    ErrorStatus.Undefined
                );
            }

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

            return new None();

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

        return new AppError(
            $"This should not happen - we cannot find any way to obtain item {code} for {Character.Schema.Name}",
            ErrorStatus.InsufficientSkill
        );
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

        if (availableInventorySpace <= 0)
        {
            return [];
        }

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

    public static async Task<List<MonsterSchema>> GetDefeatableMonstersFromList(
        PlayerCharacter Character,
        GameState gameState,
        List<MonsterSchema> monsters,
        List<DropSchema> bankItems
    )
    {
        List<MonsterSchema> monstersThatCanBeDefeated = [];

        foreach (var monster in monsters)
        {
            // For now, we assume that we cannot fight monsters a few levels above us.
            if (monster.Level > Character.Schema.Level + 2)
            {
                continue;
            }

            // TODO: For now, assume that we cannot kill bosses
            if (monster.Type == MonsterType.Boss)
            {
                continue;
            }

            var monsterIsFromEvent = gameState.EventService.IsEntityFromEvent(monster.Code);

            if (
                monsterIsFromEvent
                && gameState.EventService.WhereIsEntityActive(monster.Code) is null
            )
            {
                continue;
            }

            var fightSim = FightSimulator.FindBestFightEquipmentWithUsablePotions(
                Character,
                gameState,
                monster
            );

            if (fightSim.Outcome.ShouldFight)
            {
                monstersThatCanBeDefeated.Add(monster);
                continue;
            }

            var fightSimIfUsingWithdrawnItems =
                FightSimulator.FindBestFightEquipmentWithUsablePotions(
                    Character,
                    gameState,
                    monster,
                    bankItems
                        .Select(item => new ItemInInventory
                        {
                            Item = gameState.ItemsDict[item.Code],
                            Quantity = item.Quantity,
                        })
                        .ToList()
                );

            if (fightSimIfUsingWithdrawnItems.Outcome.ShouldFight)
            {
                monstersThatCanBeDefeated.Add(monster);
                continue;
            }
        }

        return monstersThatCanBeDefeated;
    }
}
