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
    private readonly GameState gameState;

    private const string Name = "PlayerActionService";

    private readonly ILogger<PlayerActionService> Logger;

    private readonly PlayerCharacter character;
    public readonly NavigationService NavigationService;

    public PlayerActionService(
        ILogger<PlayerActionService> logger,
        GameState gameState,
        PlayerCharacter character
    )
    {
        Logger = logger;
        this.gameState = gameState;
        this.character = character;
        NavigationService = new NavigationService(character, gameState);
    }

    public async Task<OneOf<AppError, None>> SmartItemEquip(string code, int quantity = 1)
    {
        var item = character.GetItemFromInventory(code);

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
                await character.EquipItem(code, "ring1", 1);
                await character.EquipItem(code, "ring2", 1);
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
            itemSlot = character.GetEquipmentSlot(
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
                equipmentSlot.Slot = equipmentSlot.Slot.Replace("Slot", "").FromPascalToSnakeCase();
                if (equipmentSlot.Code != "")
                {
                    // Trying to equip the same item - at the moment we don't allow using both utility slots for same item
                    if (equipmentSlot.Code == code && isUtility)
                    {
                        var amountThatCanBeAdded = MAX_AMOUNT_UTILITY_SLOT - equipmentSlot.Quantity;

                        int amountToEquip = Math.Min(amountThatCanBeAdded, quantity);

                        await character.EquipItem(code, equipmentSlot.Slot, amountToEquip);

                        return new None();
                    }
                }

                // TODO FIX
                await character.EquipItem(
                    code,
                    equipmentSlot.Slot.FromPascalToSnakeCase(),
                    quantity
                );

                break;
            case AppError appError:
                return appError;
        }

        return new None();
    }

    public async Task<None> EquipBestFightEquipment(MonsterSchema monster)
    {
        var result = FightSimulator.FindBestFightEquipment(character, gameState, monster);

        foreach (var item in result.ItemsToEquip)
        {
            await character.EquipItem(item.Code, item.Slot, item.Quantity);
        }

        return new None();
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

        foreach (var item in character.Schema.Inventory)
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
                    var equipmentSlot = character.GetEquipmentSlot(itemSlotsTheItemFits[0]);

                    if (equipmentSlot.Code == "")
                    {
                        await character.SmartItemEquip(matchingItemInInventory.Code);
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
                                $"EquipBestGatheringEquipment: Equipping \"{item.Code}\" instead of \"{equipmentSlot.Code}\" for {character.Schema.Name} for \"{skill}\""
                            );

                            await character.SmartItemEquip(matchingItemInInventory.Code);
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
            var inventorySlot = character.GetEquipmentSlot(slot)!;

            if (isUtility && inventorySlot.Code == itemCode || inventorySlot.Code == "")
            {
                itemSlot = inventorySlot;
                break;
            }
        }

        // Just take the last one, if we didn't find a better match
        return itemSlot;
    }

    public async Task<bool> CanObtainItem(ItemSchema item, int Quantity = 1)
    {
        var canObtainIt = await ObtainItem.GetJobsRequired(
            character,
            gameState,
            true,
            (await gameState.BankItemCache.GetBankItems(character, false)).Data,
            [],
            item.Code,
            Quantity,
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
        var bankItems = await gameState.BankItemCache.GetBankItems(this.character, false);

        var bankItemDict = new Dictionary<string, DropSchema>();

        foreach (var item in bankItems.Data)
        {
            // Cloning for changing the quantity
            bankItemDict.Add(item.Code, item with { });
        }

        // List<ItemSchema> items = [];

        bool itemsAreInWishlist = false;

        // foreach (var item in gameState.Items)
        // {
        //     if (character.ExistsInWishlist(item.Code))
        //     {
        //         Logger.LogInformation(
        //             $"{Name}: [{character.Schema.Name}]: GetIndividualHighPrioJob: Skipping obtaining fight items - {item.Code} is already in wish list, so we should wait until obtaining more"
        //         );

        //         itemsAreInWishlist = true;
        //         continue;
        //     }

        //     items.Add(item);
        // }

        // foreach (var item in character.Schema.Inventory)
        // {
        //     if (string.IsNullOrWhiteSpace(item.Code))
        //     {
        //         continue;
        //     }

        //     var matchingItem = gameState.ItemsDict[item.Code];

        //     if (
        //         matchingItem.Subtype != "tool"
        //         && ItemService.EquipmentItemTypes.Contains(matchingItem.Type)
        //     )
        //     {
        //         items.Add(matchingItem);
        //     }
        // }

        var bestFightItemsResult = await ItemService.GetBestFightItems(
            character,
            gameState,
            monster
        // items.Select(item => new InventorySlot { Code = item.Code, Quantity = 100 }).ToList()
        );

        List<CharacterJobAndEquipmentSlot> jobs = [];

        foreach (var item in bestFightItemsResult.Items)
        {
            var matchingItem = gameState.ItemsDict[item.Code];

            // We don't want to obtain the potions here - the FightMonster job should take care of it
            if (matchingItem.Type == "utility")
            {
                continue;
            }

            var result = character.GetEquippedItemOrInInventory(item.Code);

            (InventorySlot inventorySlot, bool isEquipped)? itemInInventory =
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

        if (jobs.Count == 0 && itemsAreInWishlist)
        {
            return null;
        }

        /**
        ** This is usually if we are just too low level to fight the monster,
        ** and we therefore cannot get the required equipment yet.
        */
        if (jobs.Count == 0 && !bestFightItemsResult.FightSimResult.Outcome.ShouldFight)
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
        int amountToUnequip = Math.Min(character.GetInventorySpaceLeft() - 5, amount);

        while (amountToUnequip > 0)
        {
            int amountToDeposit = Math.Min(character.GetInventorySpaceLeft(), amountToUnequip);

            if (amountToDeposit == 0)
            {
                break;
            }
            await character.NavigateTo("bank");
            await character.UnequipItem(
                $"Utility{utilitySlot}".FromPascalToSnakeCase(),
                amountToDeposit
            );
            await character.DepositBankItem(
                new List<WithdrawOrDepositItemRequest>
                {
                    new WithdrawOrDepositItemRequest
                    {
                        Code = itemCode,
                        Quantity = amountToDeposit,
                    },
                }
            );

            amountToUnequip = character.GetEquipmentSlot($"Utility{utilitySlot}Slot").Quantity;
        }
    }

    /**
    ** Item tasks require you to gather items from events, which might not currently be ongoing.
    */
    public async Task<bool> CanItemFromItemTaskBeObtained()
    {
        if (string.IsNullOrWhiteSpace(character.Schema.TaskType))
        {
            return true;
        }

        if (character.Schema.TaskType != "items")
        {
            return false;
        }

        ItemSchema itemFromTask = gameState.ItemsDict[character.Schema.Task];

        return await CanObtainItem(
            itemFromTask,
            character.Schema.TaskTotal - character.Schema.TaskProgress
        );
    }

    public async Task CancelTask()
    {
        if (string.IsNullOrWhiteSpace(character.Schema.Task))
        {
            return;
        }

        int tasksCoinsInInventory =
            character.GetItemFromInventory(ItemService.TasksCoin)?.Quantity ?? 0;

        if (tasksCoinsInInventory < ItemService.CancelTaskPrice)
        {
            int tasksCoinsInBank =
                (await gameState.BankItemCache.GetBankItems(character))
                    .Data.FirstOrDefault(item => item.Code == ItemService.TasksCoin)
                    ?.Quantity ?? 0;

            if (tasksCoinsInBank >= ItemService.CancelTaskPrice)
            {
                await character.NavigateTo("bank");

                await character.WithdrawBankItem(
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
            character.Schema.Task == TaskType.items.ToString() ? TaskType.items : TaskType.monsters
        ).ToString();

        await character.NavigateTo(tasksMasterCode);
    }
}

public record CharacterJobAndEquipmentSlot
{
    public required CharacterJob Job;
    public required EquipmentSlot Slot;
}
