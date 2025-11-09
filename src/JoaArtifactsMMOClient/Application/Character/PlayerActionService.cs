using Application.Artifacts.Schemas;
using Application.ArtifactsApi.Schemas;
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
    private readonly GameState GameState;

    private const string Name = "PlayerActionService";

    private readonly ILogger<PlayerActionService> Logger;

    private readonly PlayerCharacter Character;

    public PlayerActionService(
        ILogger<PlayerActionService> logger,
        GameState gameState,
        PlayerCharacter character
    )
    {
        Logger = logger;
        GameState = gameState;
        Character = character;
    }

    public async Task<OneOf<AppError, None>> NavigateTo(string code)
    {
        // We don't know what it is, but it might be an item we wish to get

        var maps = GameState.Maps.FindAll(map =>
        {
            bool matchesCode = map.Interactions.Content?.Code == code;

            if (!matchesCode)
            {
                return false;
            }

            if (map.Access.Conditions is not null)
            {
                foreach (var condition in map.Access.Conditions)
                {
                    if (
                        condition.Operator == ItemConditionOperator.AchievementUnlocked
                        && GameState.AccountAchievements.Find(achievement =>
                            achievement.Code == condition.Code
                        )
                            is null
                    )
                    {
                        return false;
                    }
                }
            }

            return true;
        });

        if (maps.Count == 0)
        {
            // TODO: Better handling
            throw new Exception($"Could not find map with code {code}");
        }

        MapSchema? closestMap = null;
        int closestCost = 0;

        foreach (var map in maps)
        {
            if (closestMap is null)
            {
                closestMap = map;
                closestCost = CalculationService.CalculateDistanceToMap(
                    Character.Schema.X,
                    Character.Schema.Y,
                    map.X,
                    map.Y
                );
                continue;
            }

            int cost = CalculationService.CalculateDistanceToMap(
                Character.Schema.X,
                Character.Schema.Y,
                map.X,
                map.Y
            );

            if (cost < closestCost)
            {
                closestMap = map;
                closestCost = cost;
            }

            // We are already standing on the map, we won't get any closer :-)
            if (cost == 0)
            {
                break;
            }
        }

        if (closestMap is null)
        {
            // TODO: Better handling
            return new AppError("Could not find closest map", ErrorStatus.NotFound);
        }

        await Character.Move(closestMap.X, closestMap.Y);

        return new None();
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

        var matchingItem = GameState.ItemsDict.ContainsKey(code) ? GameState.ItemsDict[code] : null;

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

            itemSlot = GetBestEquipmentSlotOfMultiple(itemSlotCodes, isUtility, code);
        }
        else if (matchingItem.Type == "ring")
        {
            List<string> itemSlotCodes = new List<string>
            {
                PlayerItemSlot.Ring1Slot,
                PlayerItemSlot.Ring2Slot,
            };

            itemSlot = GetBestEquipmentSlotOfMultiple(itemSlotCodes, false, code);
        }
        else if (matchingItem.Type == "artifact")
        {
            List<string> itemSlotCodes = new List<string>
            {
                PlayerItemSlot.Artifact1Slot,
                PlayerItemSlot.Artifact2Slot,
                PlayerItemSlot.Artifact3Slot,
            };

            itemSlot = GetBestEquipmentSlotOfMultiple(itemSlotCodes, false, code);
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
                equipmentSlot.Slot = equipmentSlot.Slot.Replace("Slot", "");
                if (equipmentSlot.Code != "")
                {
                    // Trying to equip the same item - at the moment we don't allow using both utility slots for same item
                    if (equipmentSlot.Code == code && isUtility)
                    {
                        var amountThatCanBeAdded = MAX_AMOUNT_UTILITY_SLOT - equipmentSlot.Quantity;

                        int amountToEquip = Math.Min(amountThatCanBeAdded, quantity);

                        await Character.EquipItem(code, equipmentSlot.Slot, amountToEquip);

                        return new None();
                    }

                    // await PlayerCharacter.UnequipItem(equipmentSlot.Slot, equipmentSlot.Quantity);
                }

                // TODO FIX
                await Character.EquipItem(
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
        var result = FightSimulator.FindBestFightEquipment(Character, GameState, monster);

        foreach (var item in result.ItemsToEquip)
        {
            await Character.EquipItem(item.Code, item.Slot, item.Quantity);
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

        if (schemaWithNewItem.MaxHp < schemaWithNewItem.Hp)
        {
            schemaWithNewItem.MaxHp = schemaWithNewItem.Hp;
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
            var matchingItemInInventory = GameState.ItemsDict.ContainsKey(item.Code)
                ? GameState.ItemsDict[item.Code]
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
                    var equippedItem = Character.GetEquipmentSlot(itemSlotsTheItemFits[0]);

                    switch (equippedItem.Value)
                    {
                        case EquipmentSlot equipmentSlot:
                            if (equipmentSlot.Code == "")
                            {
                                await Character.SmartItemEquip(matchingItemInInventory.Code);
                            }
                            else
                            {
                                var equippedItemInSlot = GameState.ItemsDict.GetValueOrNull(
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
                            break;
                        case AppError:
                            return new AppError(
                                $"Error looking up item slot \"{itemSlotsTheItemFits[0]}\""
                            );
                    }
                }
            }
        }

        return new None();
    }

    public EquipmentSlot? GetBestEquipmentSlotOfMultiple(
        List<string> itemSlotCodes,
        bool isUtility,
        string itemCode
    )
    {
        EquipmentSlot? itemSlot = null;

        foreach (var slot in itemSlotCodes)
        {
            var thisSlot = Character.GetEquipmentSlot(slot);

            switch (thisSlot.Value)
            {
                case EquipmentSlot inventorySlot:
                    itemSlot = thisSlot.AsT0;

                    if (isUtility && inventorySlot.Code == itemCode || inventorySlot.Code == "")
                    {
                        return itemSlot;
                    }
                    break;
            }
        }

        // Just take the last one, if we didn't find a better match
        return itemSlot;
    }

    public async Task<bool> CanObtainItem(ItemSchema item)
    {
        var canObtainIt = await ObtainItem.GetJobsRequired(
            Character,
            GameState,
            true,
            [],
            [],
            item.Code,
            1,
            true,
            true
        );

        switch (canObtainIt.Value)
        {
            case AppError:
                return false;
        }

        return true;
    }

    public async Task<List<CharacterJob>?> GetJobsToGetItemsToFightMonster(
        PlayerCharacter character,
        GameState gameState,
        MonsterSchema monster
    )
    {
        var itemsWithoutPotions = gameState
            .Items.FindAll(item => item.Type != "utility")
            .Select(item => new InventorySlot { Code = item.Code, Quantity = 1 })
            .ToList();

        var bestFightItems = await ItemService.GetBestFightItems(
            character,
            gameState,
            monster,
            itemsWithoutPotions
        );

        List<CharacterJob> jobs = [];

        foreach (var item in bestFightItems)
        {
            var result = character.GetEquippedItemOrInInventory(item.Code);

            (InventorySlot inventorySlot, bool isEquipped)? itemInInventory =
                result.Count > 0 ? result.ElementAt(0)! : null;

            if (itemInInventory is not null)
            {
                continue;
            }

            if (character.ExistsInWishlist(item.Code))
            {
                Logger.LogInformation(
                    $"{Name}: [{character.Schema.Name}]: GetIndividualHighPrioJob: Skipping obtaining fight items - {item.Code} is already in wish list, so we should wait until obtaining more"
                );
                return null;
            }

            var matchingItem = gameState.ItemsDict.GetValueOrNull(item.Code)!;

            if (matchingItem.Craft is null) { }

            if (!await character.PlayerActionService.CanObtainItem(matchingItem))
            {
                continue;
            }

            // Find crafter
            Logger.LogInformation(
                $"{Name}: [{character.Schema.Name}]: GetIndividualHighPrioJob: Job found - acquire {item.Code} x {1} for fighting"
            );

            character.AddToWishlist(item.Code, 1);

            jobs.Add(new ObtainOrFindItem(character, gameState, item.Code, 1));
        }

        return jobs;
    }
}
