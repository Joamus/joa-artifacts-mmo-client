using Application.Artifacts.Schemas;
using Application.ArtifactsApi.Schemas;
using Application.ArtifactsApi.Schemas.Requests;
using Application.Dtos;
using Application.Errors;
using Application.Jobs;
using Application.Services;
using Applicaton.Services.FightSimulator;
using OneOf;
using OneOf.Types;

namespace Application.Character;

/**
*
* The purpose of this class is mostly to separate some of the logic of the PlayerCharacter away
* from the PlayerCharacter class.
*/
public class PlayerActionService
{
    public static readonly int MAX_AMOUNT_UTILITY_SLOT = 100;
    public static readonly int LEVEL_DIFF_NO_XP = 10;
    public const int QUANTIY_OF_EACH_TELEPORT_POTION = 1;
    private const int MIN_FREE_BANK_SLOTS = 10;
    private readonly GameState gameState;

    private const string Name = "PlayerActionService";

    private readonly ILogger<PlayerActionService> Logger;

    private readonly PlayerCharacter Character;
    public readonly NavigationService NavigationService;

    public PlayerActionService(
        ILogger<PlayerActionService> logger,
        GameState gameState,
        PlayerCharacter character
    )
    {
        Logger = logger;
        this.gameState = gameState;
        Character = character;
        NavigationService = new NavigationService(character, gameState);
    }

    public async Task<OneOf<AppError, None>> SmartItemEquip(string code, int quantity = 1)
    {
        var item = Character.GetItemFromInventory(code);

        if (item is null)
        {
            return new AppError(
                $"Item not found in inventory with code {code}",
                ErrorStatus.NotFound
            );
        }

        var matchingItem = gameState.ItemsDict.ContainsKey(code) ? gameState.ItemsDict[code] : null;

        if (matchingItem is null)
        {
            return new AppError($"Item not found with code {code}", ErrorStatus.NotFound);
        }

        /**
         * Utility, ring, and amulet slots need different handling, because we have multiple slots that can equip the same kind of item.
         * Utility slots are even more special, because the
        */

        bool isUtility = matchingItem.Type == "utility";

        OneOf<EquipmentSlot, AppError>? itemSlot = null;
        // string slot = item.Slot;

        // We need to handle that we might have x potions already in a slot, so we should fill it up, and then equip more in another slot - we can equip up to 100 per slot
        // We can't equip the same potion in multiple slots
        // We could write utility functions for trying different item slots. It could be the same for rings, pots, and artifacts
        if (isUtility)
        {
            List<string> itemSlotCodes = new List<string>
            {
                PlayerItemSlot.Utility1Slot,
                PlayerItemSlot.Utility2Slot,
            };

            itemSlot = GetEmptyOrEquipmentSlotWithSameItem(itemSlotCodes, isUtility, code);
        }
        else if (matchingItem.Type == "ring")
        {
            if (quantity == 2)
            {
                await Character.EquipItem(
                    new EquipRequest
                    {
                        Code = code,
                        Slot = "ring1",
                        Quantity = 1,
                    }
                );
                await Character.EquipItem(
                    new EquipRequest
                    {
                        Code = code,
                        Slot = "ring2",
                        Quantity = 1,
                    }
                );
                return new None();
            }
            List<string> itemSlotCodes = new List<string>
            {
                PlayerItemSlot.Ring1Slot,
                PlayerItemSlot.Ring2Slot,
            };

            itemSlot = GetEmptyOrEquipmentSlotWithSameItem(itemSlotCodes, false, code);
        }
        else if (matchingItem.Type == "artifact")
        {
            List<string> itemSlotCodes = new List<string>
            {
                PlayerItemSlot.Artifact1Slot,
                PlayerItemSlot.Artifact2Slot,
                PlayerItemSlot.Artifact3Slot,
            };

            itemSlot = GetEmptyOrEquipmentSlotWithSameItem(itemSlotCodes, false, code);
        }
        else
        {
            itemSlot = Character.GetEquipmentSlot(
                (matchingItem.Type + "_slot").FromSnakeToPascalCase()
            );
        }

        if (itemSlot is null)
        {
            return new AppError($"Could not find item slot for item with code \"{code}\"");
        }

        switch (itemSlot.Value.Value)
        {
            case EquipmentSlot equipmentSlot:
                var pascalCaseSlot = equipmentSlot.Slot.Replace("Slot", "").FromPascalToSnakeCase();
                if (equipmentSlot.Code != "")
                {
                    // Trying to equip the same item - at the moment we don't allow using both utility slots for same item
                    if (equipmentSlot.Code == code && isUtility)
                    {
                        var amountThatCanBeAdded = MAX_AMOUNT_UTILITY_SLOT - equipmentSlot.Quantity;

                        int amountToEquip = Math.Min(amountThatCanBeAdded, quantity);

                        await Character.EquipItem(
                            new EquipRequest
                            {
                                Code = code,
                                Slot = pascalCaseSlot,
                                Quantity = amountToEquip,
                            }
                        );

                        return new None();
                    }
                }

                // TODO FIX
                await Character.EquipItem(
                    new EquipRequest
                    {
                        Code = code,
                        Slot = pascalCaseSlot,
                        Quantity = quantity,
                    }
                );

                break;
            case AppError appError:
                return appError;
        }

        return new None();
    }

    public async Task<None> EquipBestFightEquipment(MonsterSchema monster)
    {
        var result = FightSimulator.FindBestFightEquipment(Character, gameState, monster).SimResult;

        AppLogger
            .GetLogger()
            .LogInformation(
                $"EquipBestFightEquipment: [{Character.Schema.Name}]: Found {result.ItemsToEquip.Count} items to equip before fighting {monster.Code}"
            );

        List<EquipRequest> equipRequests =
        [
            .. result.ItemsToEquip.Select(item => new EquipRequest
            {
                Code = item.Code,
                Slot = item.Slot,
                Quantity = item.Quantity,
            }),
        ];

        if (equipRequests.Count > 0)
        {
            await Character.EquipItems(equipRequests);
        }

        return new None();
    }

    public async Task<CharacterJob?> GetTaskJobIfPossible(bool preferMonsterTask)
    {
        Logger.LogInformation(
            "{Name}: [{Character.Schema.Name}]: GetTaskJob: Start",
            Name,
            Character.Schema.Name
        );

        if (Character.Schema.TaskType == TaskType.monsters.ToString())
        {
            var monster = gameState.AvailableMonstersDict.GetValueOrNull(Character.Schema.Task)!;
            var nextJobResult = await GetNextJobToFightMonster(monster);

            if (nextJobResult is not null)
            {
                if (nextJobResult.Job is not null)
                {
                    Logger.LogInformation(
                        $"{Name}: [{Character.Schema.Name}]: GetTaskJob: Job found - do monster task ({monster.Code})"
                    );

                    var nextJob = nextJobResult.Job;

                    Logger.LogInformation(
                        $"{Name}: [{Character.Schema.Name}]: GetTaskJob: Doing first job to fight job for monster task - fighting {Character.Schema.TaskTotal - Character.Schema.TaskProgress} x {monster.Code} - job is {nextJob.JobName} for {nextJob.Amount} x {nextJob.Code}"
                    );
                    // Do the first job in the list, we only do one thing at a time
                    return nextJob;
                }
                else
                {
                    Logger.LogInformation(
                        $"{Name}: [{Character.Schema.Name}]: GetTaskJob: No items left to get to do monster task - fighting {Character.Schema.TaskTotal - Character.Schema.TaskProgress} x {monster.Code}"
                    );
                    return new MonsterTask(Character, gameState);
                }
            }
        }
        else if (Character.Schema.TaskType == TaskType.items.ToString())
        {
            if (await Character.PlayerActionService.CanItemFromItemTaskBeObtained())
            {
                Logger.LogInformation(
                    $"{Name}: [{Character.Schema.Name}]: GetTaskJob: Found new item task"
                );
                return new ItemTask(Character, gameState);
            }
            else if (await CancelTaskJob.CanCancelTask(Character, gameState))
            {
                await CancelTaskJob.DoCancelTask(Character, gameState);
            }
            else
            {
                return null;
            }
        }

        if (preferMonsterTask && CanHandlePotentialMonsterTasks())
        {
            Logger.LogInformation(
                "{Name}: [{character.Schema.Name}]: GetTaskJob: Found new monster task",
                Name,
                Character.Schema.Name
            );

            return new MonsterTask(Character, gameState);
        }
        if (await Character.PlayerActionService.CanItemFromItemTaskBeObtained())
        {
            Logger.LogInformation(
                $"{Name}: [{Character.Schema.Name}]: GetTaskJob: Found new item task"
            );
            return new ItemTask(Character, gameState);
        }

        Logger.LogInformation($"{Name}: [{Character.Schema.Name}]: GetTaskJob: No job found");

        return null;
    }

    public async Task<NextJobToFightResult?> GetNextJobToFightMonster(MonsterSchema monster)
    {
        var jobsToGetItems = await Character.PlayerActionService.GetJobsToGetItemsToFightMonster(
            Character,
            gameState,
            monster
        );

        // Return null if they shouldn't fight, return list of jobs if they should, return empty NextJobToFightResult
        if (jobsToGetItems is null)
        {
            return null;
        }

        if (jobsToGetItems.Count == 0)
        {
            return new NextJobToFightResult { Job = null };
        }

        var bankItems = await gameState.BankItemCache.GetBankItems(Character);

        // We assume that items that are lower level, are also easier to get (mobs less difficult to fight).
        // The issue can be that our character might only barely be able to fight the monster, so rather get the easier items first
        jobsToGetItems.Sort(
            (a, b) =>
            {
                bool aIsInBank = bankItems.Exists(item =>
                    item.Code == a.Job.Code && item.Quantity >= a.Job.Amount
                );

                bool bIsInBank = bankItems.Exists(item =>
                    item.Code == b.Job.Code && item.Quantity >= b.Job.Amount
                );

                if (aIsInBank && !bIsInBank)
                {
                    return -1;
                }
                else if (bIsInBank && !aIsInBank)
                {
                    return 1;
                }

                // If we can buy an item straight away, then let us do that first
                var aMatchingNpcItem = gameState.NpcItemsDict.ContainsKey(a.Job.Code);

                var bMatchingNpcItem = gameState.NpcItemsDict.ContainsKey(b.Job.Code);

                if (aMatchingNpcItem && !bMatchingNpcItem)
                {
                    return -1;
                }
                else if (!aMatchingNpcItem && bMatchingNpcItem)
                {
                    return 1;
                }

                var aLevel = gameState.ItemsDict.GetValueOrNull(a.Job.Code)!.Level;
                var bLevel = gameState.ItemsDict.GetValueOrNull(b.Job.Code)!.Level;

                return aLevel.CompareTo(bLevel);
            }
        );

        var nextJob = jobsToGetItems.ElementAtOrDefault(0);

        if (nextJob is not null)
        {
            nextJob.Job.onAfterSuccessEndHook = async () =>
            {
                Logger.LogInformation(
                    $"{Name}: [{Character.Name}]: onAfterSuccessEndHook: Equipping {nextJob.Job.Amount} x {nextJob.Job.Code}"
                );
                // TODO: In general, we should figure out how we handle rings/artifacts - how do we really know which item to replace? By level?
                await Character.EquipItem(
                    new EquipRequest
                    {
                        Code = nextJob.Job.Code,
                        Slot = nextJob.Slot.Slot.FromPascalToSnakeCase(),
                        Quantity = nextJob.Job.Amount,
                    }
                );
            };
        }

        return new NextJobToFightResult { Job = nextJob?.Job };
    }

    public bool CanHandlePotentialMonsterTasks()
    {
        foreach (var task in gameState.Tasks)
        {
            if (task.Type != TaskType.monsters)
            {
                continue;
            }

            var matchingMonster = gameState.AvailableMonstersDict.GetValueOrNull(task.Code)!;

            if (matchingMonster.Level > Character.Schema.Level)
            {
                continue;
            }

            if (
                !FightSimulator
                    .FindBestFightEquipmentWithUsablePotions(Character, gameState, matchingMonster)
                    .SimResult.Outcome.ShouldFight
            )
            {
                return false;
            }
        }

        return true;
    }

    public static CharacterSchema SimulateItemEquip(
        CharacterSchema characterSchema,
        ItemSchema? currentItem,
        ItemSchema newItem,
        string itemSlot,
        int? amount
    )
    {
        var type = characterSchema.GetType();

        ItemSchema? equippedItem = currentItem;

        var schemaWithNewItem = characterSchema with { };

        int initialHp = characterSchema.Hp;

        if (equippedItem is not null)
        {
            foreach (var effect in equippedItem!.Effects)
            {
                var matchingProperty = type.GetProperty(effect.Code.FromSnakeToPascalCase());

                if (matchingProperty is not null)
                {
                    int currentValue = (int)matchingProperty.GetValue(schemaWithNewItem)!;
                    matchingProperty.SetValue(schemaWithNewItem, currentValue - effect.Value);
                }
            }
        }

        foreach (var effect in newItem.Effects)
        {
            var matchingProperty = type.GetProperty(effect.Code.FromSnakeToPascalCase());

            if (matchingProperty is not null)
            {
                int currentValue = (int)matchingProperty.GetValue(schemaWithNewItem)!;
                matchingProperty.SetValue(schemaWithNewItem, currentValue + effect.Value);
            }
        }

        /**
        * Change the equipped item in the schema, both for good measure, so we can read the schema to see which items we have equipped,
        * but also so items like potions and runes will be simulated correctly, because the effects are not stat boosts, but need to be "calculated".
        * TODO: Should probably find a better way to do this, than to use reflection, for performance reasons
        */
        type.GetProperty(itemSlot)!.SetValue(schemaWithNewItem, newItem.Code);

        // For utility items
        if (amount is not null && newItem.Type == "utility")
        {
            type.GetProperty(itemSlot + "Quantity")!.SetValue(schemaWithNewItem, amount);
        }

        /**
        ** We want to ensure that the MaxHP is changed with respect to the new HP. E.g. if the new item gives us 20 more HP, then it also gives us 20 more MaxHP,
        ** and the same if it's reduced.
        */
        if (schemaWithNewItem.Hp != initialHp)
        {
            int hpDifference = initialHp - schemaWithNewItem.Hp;

            // If initial HP was higher, we want to reduce the max HP, and if lower, we want to add more HP
            schemaWithNewItem.MaxHp -= hpDifference;
        }

        return schemaWithNewItem;
    }

    public async Task<OneOf<AppError, None>> EquipBestGatheringEquipment(Skill skill)
    {
        if (!SkillService.GatheringSkills.Contains(skill))
        {
            // TODO: Invalid argument, but eh
            return new AppError($"Skill \"{skill}\" is not a valid gathering skill");
        }

        var skillName = SkillService.GetSkillName(skill)!;

        foreach (var item in Character.Schema.Inventory)
        {
            if (string.IsNullOrEmpty(item.Code))
            {
                continue;
            }
            var matchingItemInInventory = gameState.ItemsDict.ContainsKey(item.Code)
                ? gameState.ItemsDict[item.Code]
                : null;

            // Should really never happen that matchingItem is null
            if (
                matchingItemInInventory is not null
                && ItemService.IsEquipment(matchingItemInInventory.Type)
                && matchingItemInInventory.Effects.Find(effect => effect.Code == skillName)
                    is not null
            )
            {
                var itemInInventoryEffect = matchingItemInInventory.Effects.Find(effect =>
                    effect.Code == skillName
                );

                if (itemInInventoryEffect is null)
                {
                    continue;
                }

                var itemSlotsTheItemFits = ItemService.GetItemSlotsFromItemType(
                    matchingItemInInventory.Type
                );

                // For now we assume that items that can fit in multiple slots, e.g. ring 1, 2, 3, etc won't have these effects.
                // It would be cool to be able to handle it, but it will mean that we have to swap around items potentially, etc.
                // e.g. Ring 1 and 2 both have a bonus to alchemy, then we want to make sure that we only swap out the ring with the
                // lowest bonus, if the ring we are evaluating has higher than any of the other.
                if (itemSlotsTheItemFits.Count() > 0)
                {
                    var equipmentSlot = Character.GetEquipmentSlot(itemSlotsTheItemFits[0]);

                    if (equipmentSlot.Code == "")
                    {
                        await Character.SmartItemEquip(matchingItemInInventory.Code);
                    }
                    else
                    {
                        var equippedItemInSlot = gameState.ItemsDict.GetValueOrNull(
                            equipmentSlot.Code
                        );

                        if (equippedItemInSlot is null)
                        {
                            return new AppError(
                                $"Matching item in inventory is null for code \"{equipmentSlot.Code}\""
                            );
                        }

                        var equippedItemValue =
                            equippedItemInSlot
                                .Effects.Find(effect => effect.Code == skillName)
                                ?.Value ?? 0;

                        // For gathering skills, the lower value, the better, e.g. -10 alchemy means 10% faster gathering
                        if (equippedItemValue > itemInInventoryEffect.Value)
                        {
                            Logger.LogInformation(
                                $"EquipBestGatheringEquipment: Equipping \"{item.Code}\" instead of \"{equipmentSlot.Code}\" for {Character.Schema.Name} for \"{skill}\""
                            );

                            await Character.SmartItemEquip(matchingItemInInventory.Code);
                        }
                    }
                }
            }
        }

        return new None();
    }

    public EquipmentSlot? GetEmptyOrEquipmentSlotWithSameItem(
        List<string> itemSlotCodes,
        bool isUtility,
        string itemCode
    )
    {
        EquipmentSlot? itemSlot = null;

        foreach (var slot in itemSlotCodes)
        {
            var inventorySlot = Character.GetEquipmentSlot(slot)!;

            if (isUtility && inventorySlot.Code == itemCode || inventorySlot.Code == "")
            {
                itemSlot = inventorySlot;
                break;
            }
        }

        // Just take the last one, if we didn't find a better match
        return itemSlot;
    }

    public async Task<bool> CanObtainItem(
        ItemSchema item,
        int Quantity = 1,
        bool allowTriggerTraining = true
    )
    {
        var canObtainIt = await ObtainItem.GetJobsRequired(
            Character,
            gameState,
            true,
            item.Code,
            Quantity,
            true,
            allowTriggerTraining,
            true
        );

        switch (canObtainIt.Value)
        {
            case AppError:
                return false;
        }

        return true;
    }

    public async Task<List<CharacterJobAndEquipmentSlot>?> GetJobsToGetItemsToFightMonster(
        PlayerCharacter character,
        GameState gameState,
        MonsterSchema monster
    )
    {
        var bankItems = await gameState.BankItemCache.GetBankItems(this.Character, false);

        var bankItemDict = new Dictionary<string, DropSchema>();

        foreach (var item in bankItems)
        {
            // Cloning for changing the quantity
            bankItemDict.Add(item.Code, item with { });
        }

        var bestFightItemsResult = await ItemService.GetBestFightItemsFromObtainableItems(
            character,
            gameState,
            monster,
            bankItemDict
        );

        // We only try to disprove this, if we have items to go through
        bool allItemsAreInWishlist = bestFightItemsResult.Items.Count > 0;

        List<CharacterJobAndEquipmentSlot> jobs = [];

        foreach (var item in bestFightItemsResult.Items)
        {
            var matchingItem = gameState.ItemsDict[item.Code];

            // We don't want to obtain the potions here - the FightMonster job should take care of it
            if (matchingItem.Type == "utility")
            {
                continue;
            }

            if (character.ExistsInWishlist(item.Code))
            {
                continue;
            }
            else
            {
                allItemsAreInWishlist = false;
            }

            var result = character.GetEquippedItemOrInInventory(item.Code);

            (EquipmentSlot inventorySlot, bool isEquipped)? itemInInventory =
                result.Count > 0 ? result.ElementAt(0)! : null;

            int amountToObtain = item.Quantity;

            if (itemInInventory is not null)
            {
                if (itemInInventory.Value.inventorySlot.Quantity >= amountToObtain)
                {
                    continue;
                }

                amountToObtain -= itemInInventory.Value.inventorySlot.Quantity;
            }

            // Find crafter
            Logger.LogInformation(
                $"{Name}: [{character.Schema.Name}]: GetIndividualHighPrioJob: Job found - acquire {item.Code} x {1} for fighting"
            );

            jobs.Add(
                new CharacterJobAndEquipmentSlot
                {
                    Job = new ObtainOrFindItem(character, gameState, item.Code, amountToObtain),
                    Slot = item,
                }
            );
        }

        /**
        ** This is usually if we are just too low level to fight the monster,
        ** and we therefore cannot get the required equipment yet.
        */
        if (
            jobs.Count == 0
            && (allItemsAreInWishlist || !bestFightItemsResult.FightSimResult.Outcome.ShouldFight)
        )
        {
            return null;
        }

        int itemsAmount = 0;
        int slotAmount = 0;

        foreach (var job in jobs)
        {
            itemsAmount += job.Job.Amount;
            slotAmount += 1;
        }

        return jobs;
    }

    // public async void BuyItemFromNpc(string code, int quantity) { }

    public async Task DepositPotions(int utilitySlot, string itemCode, int amount)
    {
        int amountToUnequip = Math.Min(Character.GetAvailableInventorySpace() - 5, amount);

        while (amountToUnequip > 0)
        {
            int amountToDeposit = Math.Min(Character.GetAvailableInventorySpace(), amountToUnequip);

            if (amountToDeposit == 0)
            {
                break;
            }
            await Character.NavigateTo("bank");
            await Character.UnequipItem(
                new UnequipRequest
                {
                    Slot = $"Utility{utilitySlot}".FromPascalToSnakeCase(),
                    Quantity = amountToDeposit,
                }
            );
            await Character.DepositBankItem(
                new List<WithdrawOrDepositItemRequest>
                {
                    new WithdrawOrDepositItemRequest
                    {
                        Code = itemCode,
                        Quantity = amountToDeposit,
                    },
                }
            );

            amountToUnequip = Character.GetEquipmentSlot($"Utility{utilitySlot}Slot").Quantity;
        }
    }

    /**
    ** Item tasks require you to gather items from events, which might not currently be ongoing.
    */
    public async Task<bool> CanItemFromItemTaskBeObtained()
    {
        if (string.IsNullOrWhiteSpace(Character.Schema.TaskType))
        {
            return true;
        }

        if (Character.Schema.TaskType != "items")
        {
            return false;
        }

        ItemSchema itemFromTask = gameState.ItemsDict[Character.Schema.Task];

        if (
            await CancelTaskJob.ShouldCancelTask(gameState, itemFromTask)
            // This is mostly to prevent having to gather fish that we are too low level to catch, but high enough cooking level to cook
            || !await CanObtainItem(
                itemFromTask,
                Character.Schema.TaskTotal - Character.Schema.TaskProgress,
                false
            )
        )
        {
            return false;
        }

        return true;
    }

    public async Task CancelTask()
    {
        if (string.IsNullOrWhiteSpace(Character.Schema.Task))
        {
            return;
        }

        int tasksCoinsInInventory =
            Character.GetItemFromInventory(ItemService.TasksCoin)?.Quantity ?? 0;

        if (tasksCoinsInInventory < ItemService.CancelTaskPrice)
        {
            int tasksCoinsInBank =
                (await gameState.BankItemCache.GetBankItems(Character))
                    .FirstOrDefault(item => item.Code == ItemService.TasksCoin)
                    ?.Quantity ?? 0;

            if (tasksCoinsInBank >= ItemService.CancelTaskPrice)
            {
                await Character.NavigateTo("bank");

                await Character.WithdrawBankItem(
                    new List<WithdrawOrDepositItemRequest>
                    {
                        new WithdrawOrDepositItemRequest
                        {
                            Code = ItemService.TasksCoin,
                            Quantity = ItemService.CancelTaskPrice,
                        },
                    }
                );
            }
        }

        string tasksMasterCode = (
            Character.Schema.Task == TaskType.items.ToString() ? TaskType.items : TaskType.monsters
        ).ToString();

        await Character.NavigateTo(tasksMasterCode);
    }

    public async Task DepositAllItems()
    {
        var items = Character
            .Schema.Inventory.Where(item => !string.IsNullOrWhiteSpace(item.Code))
            .ToList();

        if (items.Count == 0)
        {
            return;
        }

        await Character.NavigateTo("bank");

        await Character.DepositBankItem(
            items
                .Where(item => !string.IsNullOrWhiteSpace(item.Code))
                .Select(item => new WithdrawOrDepositItemRequest
                {
                    Code = item.Code,
                    Quantity = item.Quantity,
                })
                .ToList()
        );
    }

    public async Task WithdrawTeleportPotions()
    {
        var bankItems = await gameState.BankItemCache.GetBankItems(Character);

        foreach (var item in bankItems)
        {
            if (string.IsNullOrWhiteSpace(item.Code))
            {
                continue;
            }

            var matchingItem = gameState.ItemsDict[item.Code];

            if (!ItemService.IsTeleportPotion(matchingItem))
            {
                continue;
            }

            int quantityInInventory = Character.GetItemFromInventory(item.Code)?.Quantity ?? 0;

            if (quantityInInventory >= QUANTIY_OF_EACH_TELEPORT_POTION)
            {
                continue;
            }

            int amountNeeded = QUANTIY_OF_EACH_TELEPORT_POTION - quantityInInventory;

            int amountToWithdraw = Math.Min(amountNeeded, item.Quantity);

            if (amountToWithdraw > 0)
            {
                await Character.NavigateTo("bank");
                await Character.WithdrawBankItem(
                    [
                        new WithdrawOrDepositItemRequest
                        {
                            Code = item.Code,
                            Quantity = amountToWithdraw,
                        },
                    ]
                );
            }
        }
    }

    public async Task WithdrawAndUseConsumableBags()
    {
        var bankItems = await gameState.BankItemCache.GetBankItems(Character);

        foreach (var item in bankItems)
        {
            if (string.IsNullOrWhiteSpace(item.Code))
            {
                continue;
            }

            var matchingItem = gameState.ItemsDict[item.Code];

            if (
                matchingItem.Subtype == "bag"
                && matchingItem.Type == "consumable"
                && Character.GetAvailableInventorySpace() >= item.Quantity
                && Character.GetAvailableInventorySlots() > 0
            )
            {
                await Character.NavigateTo("bank");
                await Character.WithdrawBankItem(
                    [
                        new WithdrawOrDepositItemRequest
                        {
                            Code = item.Code,
                            Quantity = item.Quantity,
                        },
                    ]
                );
            }
        }

        foreach (var item in Character.Schema.Inventory)
        {
            if (string.IsNullOrWhiteSpace(item.Code))
            {
                continue;
            }

            var matchingItem = gameState.ItemsDict[item.Code];

            if (matchingItem.Subtype == "bag" && matchingItem.Type == "consumable")
            {
                await Character.UseItem(item.Code, item.Quantity);
            }
        }
    }

    public async Task BuyBankSpaceIfNeeded()
    {
        var result = await gameState.BankItemCache.GetBankDetails();

        if (result.NextExpansionCost < Character.Schema.Gold + result.Gold)
        {
            var itemsInBank = await gameState.BankItemCache.GetBankItems(null);

            int amountFree = result.Slots - itemsInBank.Count;

            if (amountFree <= MIN_FREE_BANK_SLOTS)
            {
                int amountNeededToWithdraw =
                    result.NextExpansionCost > Character.Schema.Gold
                        ? result.NextExpansionCost - Character.Schema.Gold
                        : 0;

                if (amountNeededToWithdraw > 0)
                {
                    Logger.LogInformation(
                        $"[{Character.Schema.Name}] withdrawing {amountNeededToWithdraw} to buy bank expansions"
                    );
                    await Character.WithdrawBankGold(amountNeededToWithdraw);
                }

                Logger.LogInformation(
                    $"[{Character.Schema.Name}] buying bank expansions, free bank slots is {amountFree} - got {Character.Schema.Gold} gold, next expansion costs {result.NextExpansionCost}"
                );
                // Buy bank expansion
                await Character.NavigateTo("bank");
                await Character.BuyBankExpansion(Character.Schema.Name);
            }
        }
    }

    public static string GetBestNonCombatEffectWithLevelDiff(int levelDifference)
    {
        return levelDifference > LEVEL_DIFF_NO_XP ? Effect.Prospecting : Effect.Wisdom;
    }
}

public record CharacterJobAndEquipmentSlot
{
    public required CharacterJob Job;
    public required EquipmentSlot Slot;
}

public record NextJobToFightResult
{
    public required CharacterJob? Job;
}
