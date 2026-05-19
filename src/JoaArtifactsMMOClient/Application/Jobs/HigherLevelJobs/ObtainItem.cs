using Application.ArtifactsApi.Schemas;
using Application.ArtifactsApi.Schemas.Responses;
using Application.Character;
using Application.Dtos;
using Application.Errors;
using Application.Records;
using Application.Services;
using Applicaton.Jobs;
using Applicaton.Services.FightSimulator;
using Newtonsoft.Json;
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
            Code,
            Amount,
            AllowUsingMaterialsFromInventory,
            CanTriggerTraining
        );

        switch (result.Value)
        {
            case AppError jobError:
                return jobError;
            case List<CharacterJob> resultJobs:
                jobs = resultJobs;
                break;
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
    public static async Task<OneOf<AppError, List<CharacterJob>>> GetJobsRequired(
        PlayerCharacter Character,
        GameState gameState,
        bool allowUsingItemFromBank,
        string code,
        int amount,
        bool allowUsingItemFromInventory = false,
        bool canTriggerTraining = false,
        bool ignoreInventoryFull = false
    )
    {
        var bankItems = (await gameState.BankItemCache.GetBankItems(Character, false)).Data;

        List<CharacterJob> jobs = [];

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
            bankItems
                .Select(item => new DropSchema { Code = item.Code, Quantity = item.Quantity })
                .ToList(),
            jobs,
            code,
            amount,
            allowUsingItemFromInventory,
            canTriggerTraining,
            true,
            ignoreInventoryFull
        );

        return result.Value switch
        {
            AppError appError => (OneOf<AppError, List<CharacterJob>>)appError,
            _ => (OneOf<AppError, List<CharacterJob>>)jobs,
        };
    }

    /**
     * Get all the jobs required to obtain an item
     * We mutate a list to recursively add all the required jobs to the list
    */
    static async Task<OneOf<AppError, None>> InnerGetJobsRequired(
        PlayerCharacter character,
        List<DropSchema> itemsInInventory,
        GameState gameState,
        bool allowUsingItemFromBank,
        List<DropSchema> itemsInBankClone,
        List<CharacterJob> jobs,
        string code,
        int amount,
        bool allowUsingItemFromInventory = false,
        bool canTriggerTraining = false,
        bool firstIteration = true,
        bool ignoreInventoryFull = false
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
                jobs.Add(new WithdrawItem(character, gameState, code, amountToTakeFromBank, false));
                matchingItemInBank!.Quantity -= amountToTakeFromBank;

                amount -= amountToTakeFromBank;
            }
        }

        int requiredAmount = amount - (itemFromInventory?.Quantity ?? 0);

        if (requiredAmount <= 0)
        {
            return new None();
        }

        var resourceResult = ObtainResourceRelatedJob(
            gameState,
            character,
            code,
            requiredAmount,
            canTriggerTraining
        );

        if (resourceResult is not null)
        {
            return resourceResult.Value.Match<OneOf<AppError, None>>(
                error =>
                {
                    return error;
                },
                job =>
                {
                    jobs.Add(job);
                    return new None();
                }
            );
        }

        var craftItemResult = await ObtainCraftItemRelatedJob(
            character,
            gameState,
            matchingItem,
            itemsInInventory,
            allowUsingItemFromBank,
            itemsInBankClone,
            code,
            requiredAmount,
            canTriggerTraining,
            ignoreInventoryFull
        );

        if (craftItemResult is not null)
        {
            return craftItemResult.Value.Match<OneOf<AppError, None>>(
                error =>
                {
                    return error;
                },
                craftJobs =>
                {
                    foreach (var job in craftJobs)
                    {
                        jobs.Add(job);
                    }

                    return new None();
                }
            );
        }
        var matchingNpcItem = gameState.NpcItemsDict.GetValueOrNull(matchingItem.Code);

        var taskCoinsResult = matchingNpcItem is not null
            ? await ObtainTaskCoinsRelatedJob(
                character,
                gameState,
                matchingItem,
                matchingNpcItem,
                itemsInInventory,
                itemsInBankClone,
                code,
                requiredAmount
            )
            : null;

        if (taskCoinsResult is not null)
        {
            return taskCoinsResult.Value.Match<OneOf<AppError, None>>(
                error =>
                {
                    return error;
                },
                taskCoinsJobs =>
                {
                    foreach (var job in taskCoinsJobs)
                    {
                        jobs.Add(job);
                    }

                    return new None();
                }
            );
        }

        var monsterDropsResult = await ObtainMonsterDropsRelatedJob(
            character,
            gameState,
            itemsInBankClone,
            code,
            requiredAmount
        );

        if (monsterDropsResult is not null)
        {
            return monsterDropsResult.Value.Match<OneOf<AppError, None>>(
                error =>
                {
                    return error;
                },
                job =>
                {
                    jobs.Add(job);

                    return new None();
                }
            );
        }

        var npcItemsResult = matchingNpcItem is not null
            ? await ObtainNpcItemRelatedJob(
                character,
                gameState,
                matchingItem,
                matchingNpcItem,
                itemsInBankClone,
                code,
                requiredAmount
            )
            : null;

        if (npcItemsResult is not null)
        {
            return npcItemsResult.Value.Match<OneOf<AppError, None>>(
                error =>
                {
                    return error;
                },
                npcItemJobs =>
                {
                    foreach (var job in npcItemJobs)
                    {
                        jobs.Add(job);
                    }

                    return new None();
                }
            );
        }

        return new AppError(
            $"This should not happen - we cannot find any way to obtain item {code} for {character.Schema.Name}",
            ErrorStatus.InsufficientSkill
        );
    }

    // TODO: Make the iterations into something like "craftPerIteration", so it returns a list of tuples or something,
    // e.g if you have to create 10 iron bars, it might be, 3, 3, 3, 1 or something
    public static List<int> CalculateObtainItemIterations(
        ItemSchema item,
        int freeInventorySpace,
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
        int availableInventorySpace = freeInventorySpace - 5;

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

    static OneOf<AppError, CharacterJob>? ObtainResourceRelatedJob(
        GameState gameState,
        PlayerCharacter character,
        string code,
        int requiredAmount,
        bool canTriggerTraining
    )
    {
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
            var gatherJob = new GatherResourceItem(character, gameState, code, requiredAmount)
            {
                CanTriggerTraining = canTriggerTraining,
            };

            return gatherJob;
            // jobs.Add(gatherJob);
            // return new None();
        }

        return null;
    }

    static async Task<OneOf<AppError, List<CharacterJob>>?> ObtainCraftItemRelatedJob(
        PlayerCharacter character,
        GameState gameState,
        ItemSchema matchingItem,
        List<DropSchema> itemsInInventory,
        bool allowUsingItemFromBank,
        List<DropSchema> itemsInBank,
        string code,
        int requiredAmount,
        bool canTriggerTraining = false,
        bool ignoreInventoryFull = false
    )
    {
        List<CharacterJob> jobs = [];

        if (matchingItem.Craft is null)
        {
            return null;
        }

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
            ignoreInventoryFull
                ? character.Schema.InventoryMaxItems
                : character.GetInventorySpaceLeft(),
            requiredAmount
        );

        if (iterations.Count == 0)
        {
            return new AppError("Inventory does not have enough space", ErrorStatus.InventoryFull);
        }

        foreach (var iterationAmount in iterations)
        {
            foreach (var item in matchingItem.Craft.Items)
            {
                int itemAmount = item.Quantity * iterationAmount;

                var result = await InnerGetJobsRequired(
                    character,
                    itemsInInventory,
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
            var craftItemJob = new CraftItem(character, gameState, code, iterationAmount)
            {
                CanTriggerTraining = canTriggerTraining,
            };

            jobs.Add(craftItemJob);
        }

        return jobs;
    }

    static async Task<OneOf<AppError, List<CharacterJob>>?> ObtainTaskCoinsRelatedJob(
        PlayerCharacter character,
        GameState gameState,
        ItemSchema matchingItem,
        NpcItemSchema matchingNpcItem,
        List<DropSchema> itemsInInventory,
        List<DropSchema> itemsInBank,
        string code,
        int requiredAmount
    )
    {
        List<CharacterJob> jobs = [];

        if (matchingItem.Code == ItemService.TasksCoin)
        {
            jobs.Add(
                new DoTaskUntilObtainedItem(
                    character,
                    gameState,
                    TaskType.items,
                    matchingItem.Code,
                    requiredAmount
                )
            );

            return jobs;
        }

        if (matchingItem.Subtype != "task")
        {
            return null;
        }

        // BuyPrice should not be null here - this is how you obtain task items.
        int taskCoinsNeeded = (matchingNpcItem!.BuyPrice ?? 0) * requiredAmount;
        int taskCoinsNeededFromInventory = taskCoinsNeeded;

        var taskCoinsInInventory = itemsInInventory.FirstOrDefault(item =>
            item.Code == ItemService.TasksCoin
        );

        var taskCoinsInBank = itemsInBank.FirstOrDefault(item =>
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
                    character,
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
            jobs.Add(new BuyItemNpc(character, gameState, code, requiredAmount, true, true, true));
            return jobs;
        }

        // Pick up a task, or complete one you have
        if (character.Schema.TaskType == "monsters")
        {
            var monster = gameState.AvailableMonstersDict.GetValueOrDefault(character.Schema.Task);

            if (monster is null)
            {
                return new AppError(
                    $"Monster with code {code} was not found",
                    ErrorStatus.NotFound
                );
            }

            if (
                FightSimulator
                    .CalculateFightOutcome(character.Schema, monster, gameState)
                    .ShouldFight
            )
            {
                jobs.Add(new MonsterTask(character, gameState, matchingItem.Code, requiredAmount));
                return jobs;
            }

            return new AppError(
                $"You cannot obtain item with code {code}, because you need to complete your monster task, and you cannot beat the monster",
                ErrorStatus.InsufficientSkill
            );
        }
        else if (
            string.IsNullOrEmpty(character.Schema.Task)
            || await character.PlayerActionService.CanItemFromItemTaskBeObtained()
        )
        {
            jobs.Add(
                new DoTaskUntilObtainedItem(
                    character,
                    gameState,
                    TaskType.items,
                    matchingItem.Code,
                    requiredAmount
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

        return jobs;
    }

    static async Task<OneOf<AppError, CharacterJob>?> ObtainMonsterDropsRelatedJob(
        PlayerCharacter character,
        GameState gameState,
        List<DropSchema> itemsInBank,
        string code,
        int requiredAmount
    )
    {
        List<MonsterSchema> suitableMonsters = [];

        var monstersThatDropTheItem = gameState.AvailableMonsters.FindAll(monster =>
            monster.Drops.Find(drop => drop.Code == code) is not null
        );

        if (monstersThatDropTheItem.Count == 0)
        {
            return null;
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

        var monstersWeCanDefeatThatDropTheItem = await GetDefeatableMonstersFromList(
            character,
            gameState,
            monstersThatDropTheItem,
            itemsInBank
        );

        if (monstersWeCanDefeatThatDropTheItem.Count > 0)
        {
            monstersWeCanDefeatThatDropTheItem.Sort((a, b) => a.Level - b.Level);

            lowestLevelMonster = monstersWeCanDefeatThatDropTheItem.ElementAt(0);
        }

        if (monstersThatDropTheItem.Count > 0 && monstersWeCanDefeatThatDropTheItem.Count == 0)
        {
            return new AppError(
                $"The item is a monster drop, but we cannot defeat the monsters that drop it"
            );
        }

        if (lowestLevelMonster is not null)
        {
            List<WithdrawItem> withdrawItemJobs =
                await FightMonster.GetWithdrawItemJobsIfBetterItemsInBank(
                    character,
                    gameState,
                    lowestLevelMonster
                );
            var fightSimIfUsingWithdrawnItems =
                FightSimulator.FindBestFightEquipmentWithUsablePotions(
                    character,
                    gameState,
                    lowestLevelMonster,
                    [
                        .. itemsInBank.Select(item => new ItemInInventory
                        {
                            Item = gameState.ItemsDict[item.Code],
                            Quantity = item.Quantity,
                        }),
                    ]
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
                    character,
                    gameState,
                    lowestLevelMonster.Code,
                    requiredAmount,
                    code
                );

                // job.AllowUsingMaterialsFromInventory = true;
                return job;
            }
        }

        if (monstersWeCanDefeatThatDropTheItem.Count > 0)
        {
            if (foundMonsterThatIsFromEvent)
            {
                return new AppError(
                    $"The monster that drops {code} is likely from an event, but the event is not active - {character.Schema.Name} cannot obtain {code}",
                    ErrorStatus.InsufficientSkill
                );
            }
            else
            {
                return new AppError(
                    $"Cannot fight any monsters that drop item {code} - {character.Schema.Name} would lose",
                    ErrorStatus.InsufficientSkill
                );
            }
        }

        return null;
    }

    static async Task<OneOf<AppError, List<CharacterJob>>?> ObtainNpcItemRelatedJob(
        PlayerCharacter character,
        GameState gameState,
        ItemSchema matchingItem,
        NpcItemSchema matchingNpcItem,
        List<DropSchema> itemsInBank,
        string code,
        int requiredAmount
    )
    {
        List<CharacterJob> jobs = [];

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

        var npcIsAccessible = gameState.AvailableNpcs.Exists(npc =>
            npc.Code == matchingNpcItem.Npc
        );

        if (!npcIsAccessible)
        {
            return new AppError(
                $"NPC \"{matchingNpcItem.Code}\" is not accessible",
                ErrorStatus.Undefined
            );
        }

        int amountOfCurrency =
            matchingNpcItem.Currency == "gold"
                ? character.Schema.Gold
                : character.GetItemFromInventory(matchingNpcItem.Currency)?.Quantity ?? 0;

        int neededCurrency = (int)matchingNpcItem.BuyPrice * requiredAmount;

        if (matchingNpcItem.Currency == "gold" && neededCurrency > amountOfCurrency)
        {
            return new AppError(
                $"Matching item costs more gold than the character currently has - cannot obtain",
                ErrorStatus.Undefined
            );
        }

        if (amountOfCurrency < neededCurrency)
        {
            bool canObtainCurrencyFromMonsters = true;

            var amountInBank =
                itemsInBank.FirstOrDefault(item => item.Code == matchingNpcItem.Currency)?.Quantity
                ?? 0;

            if (amountInBank > 0)
            {
                int amountToWithdraw =
                    amountInBank >= neededCurrency ? neededCurrency : amountInBank;

                jobs.Add(
                    new WithdrawItem(
                        character,
                        gameState,
                        matchingNpcItem.Currency,
                        amountToWithdraw
                    )
                );

                neededCurrency -= amountToWithdraw;

                if (neededCurrency < 0)
                {
                    // Should not happen
                    neededCurrency = 0;
                }
            }

            if (matchingNpcItem.Currency != "gold" && neededCurrency > 0)
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
                    character,
                    gameState,
                    monstersThatDropCurrency,
                    itemsInBank
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

            if (neededCurrency > 0)
            {
                jobs.Add(
                    new ObtainOrFindItem(
                        character,
                        gameState,
                        matchingNpcItem.Currency,
                        neededCurrency - amountOfCurrency
                    )
                );
            }
        }

        jobs.Add(
            new BuyItemNpc(
                character,
                gameState,
                matchingItem.Code,
                requiredAmount,
                true,
                true,
                true
            )
        );

        return jobs;
    }
}
