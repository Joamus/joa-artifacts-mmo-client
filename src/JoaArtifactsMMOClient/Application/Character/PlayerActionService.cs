using System.Diagnostics.Eventing.Reader;
using System.Security.Permissions;
using Application;
using Application.ArtifactsApi.Schemas;
using Application.ArtifactsApi.Schemas.Responses;
using Application.Character;
using Application.Errors;
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
    private static readonly int MAX_AMOUNT_UTILITY_SLOT = 100;
    private readonly GameState GameState;

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

    public async Task<OneOf<AppError, None>> NavigateTo(string code, ContentType contentType)
    {
        // We don't know what it is, but it might be an item we wish to get

        if (contentType == ContentType.Resource)
        {
            var resources = GameState.Resources.FindAll(resource =>
                resource.Drops.Find(drop => drop.Code == code && drop.Rate > 0) != null
            );

            ResourceSchema? bestResource = null;
            int bestDropRate = 0;

            // The higher the drop rate, the lower the number. Drop rate of 1 means 100% chance, rate of 10 is 10% chance, rate of 100 is 1%

            foreach (var resource in resources)
            {
                if (bestDropRate == 0)
                {
                    bestResource = resource;
                    bestDropRate = resource.Drops[0].Rate;
                    continue;
                }
                var bestDrop = resource.Drops.Find(drop =>
                    drop.Code == code && drop.Rate < bestDropRate
                );

                if (bestDrop is not null)
                {
                    bestDropRate = bestDrop.Rate;
                    bestResource = resource;
                }
            }

            if (bestResource is null)
            {
                throw new Exception($"Could not find map with resource {code}");
            }

            code = bestResource.Code;
        }

        var maps = GameState.Maps.FindAll(map =>
            map.Content is not null && map.Content.Code == code
        );

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

        if (matchingItem.Type == "ring")
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

    // public OneOf<AppError, List<EquipmentSlot>> FindBestFightEquipment(MonsterSchema monster)
    // {
    //     // ItemSchema? bestItemCandidate = GameState.ItemsDict.GetValueOrNull(
    //     //     PlayerCharacter.Schema.WeaponSlot
    //     // );

    //     // if (bestItemCandidate is null)
    //     // {
    //     //     return new AppError(
    //     //         $"Currently best weapon with code \"{PlayerCharacter.Schema.WeaponSlot}\" is null"
    //     //     );
    //     // }

    //     // string initialItemCode = bestItemCandidate.Code;

    //     // For now, we only check if we have better weapons
    //     //
    //     List<(string, string)> equipmentTypes =
    //     [
    //         ("weapon", "WeaponSlot"),
    //         ("body_armor", "BodyArmorSlot"),
    //         ("leg_armor", "LegArmorSlot"),
    //         ("helmet", "HelmetSlot"),
    //         ("boots", "BootsSlot"),
    //         ("ring", "Ring1Slot"),
    //         ("ring", "Ring2Slot"),
    //         ("amulet", "AmuletSlot"),
    //         ("shield", "ShieldSlot"),
    //     ];

    //     List<EquipmentSlot> itemsToEquip = [];

    //     /*
    //       This might not be the most optimal, but basically we go through each item type one by one, and find the best fit for every item to equip.
    //       There are definitely cases we don't handle super well by doing this, because the characer might have a fire weapon, that will be better
    //       with a specific armor set, because it gives more fire damage, but we will never consider that scenario, because the fire weapon might be
    //       disqualified in the "weapon" round, because it's not the best item.

    //       We will need a recursive function that calculates all combinations, but for now, this will be good enough to ensure that the characters
    //       put on their equipment before fighting, if they have any in their inventory, and in general will use decent equipment.
    //     */
    //     var bestSchemaCandiate = Character.Schema with
    //     { };
    //     var bestFightResult = FightSimulator.CalculateFightOutcome(bestSchemaCandiate, monster);

    //     int bestItemAmount = 1;

    //     foreach (var (equipmentType, equipmentSlot) in equipmentTypes)
    //     {
    //         var items = Character.GetItemsFromInventoryWithType(equipmentType);

    //         if (items.Count == 0)
    //         {
    //             continue;
    //         }

    //         // var equippedItem = PlayerCharacter.GetEquipmentSlot(equipmentSlot);
    //         EquipmentSlot? equippedItem = null;

    //         switch (Character.GetEquipmentSlot(equipmentSlot).Value)
    //         {
    //             case AppError error:
    //                 return error;
    //             case EquipmentSlot slot:
    //                 equippedItem = slot;
    //                 break;
    //         }

    //         ItemSchema? bestItemCandidate = GameState.ItemsDict.GetValueOrNull(equippedItem!.Code);

    //         if (bestItemCandidate is null)
    //         {
    //             return new AppError(
    //                 $"Currently best weapon with code \"{Character.Schema.WeaponSlot}\" is null"
    //             );
    //         }

    //         string initialItemCode = bestItemCandidate.Code;

    //         foreach (var item in items)
    //         {
    //             ItemSchema? itemSchema = GameState.ItemsDict.GetValueOrNull(item.Item.Code);

    //             if (itemSchema is null)
    //             {
    //                 return new AppError(
    //                     $"Current weapon with code \"{item.Item.Code}\" is null - should never happen"
    //                 );
    //             }

    //             if (!ItemService.CanUseItem(itemSchema, Character.Schema.Level))
    //             {
    //                 continue;
    //             }

    //             var characterSchema = bestSchemaCandiate with { };

    //             characterSchema = SimulateItemEquip(
    //                 characterSchema,
    //                 bestItemCandidate,
    //                 itemSchema,
    //                 equipmentSlot,
    //                 1
    //             );

    //             var fightOutcome = FightSimulator.CalculateFightOutcome(characterSchema, monster);

    //             if (
    //                 fightOutcome.Result == FightResult.Win
    //                 && (
    //                     bestFightResult.Result != FightResult.Win
    //                     || fightOutcome.PlayerHp > bestFightResult.PlayerHp
    //                 )
    //             )
    //             {
    //                 bestFightResult = fightOutcome;
    //                 bestItemCandidate = item.Item;
    //                 bestSchemaCandiate = characterSchema;
    //                 bestItemAmount = item.Item.Subtype == "utility" ? item.Quantity : 1;
    //             }
    //         }

    //         if (initialItemCode != bestItemCandidate.Code)
    //         {
    //             string snakeCaseSlot = equipmentSlot.FromPascalToSnakeCase();

    //             Logger.LogInformation(
    //                 $"FindBestFightEquipment: Should swap \"{initialItemCode}\" -> \"{bestItemCandidate.Code}\" in slot \"{snakeCaseSlot}\" for {Character.Schema.Name} when fighting \"{monster.Code}\""
    //             );

    //             itemsToEquip.Add(
    //                 new EquipmentSlot
    //                 {
    //                     Code = bestItemCandidate.Code,
    //                     Slot = snakeCaseSlot,
    //                     Quantity = bestItemAmount,
    //                 }
    //             );
    //         }
    //     }

    //     return itemsToEquip;
    // }

    public async Task<None> EquipBestFightEquipment(MonsterSchema monster)
    {
        var result = FightSimulator.FindBestFightEquipment(Character, GameState, monster);

        foreach (var item in result.Item3)
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
            type.GetProperty(itemSlot + "_quantity")!.SetValue(schemaWithNewItem, amount);
        }

        if (schemaWithNewItem.MaxHp < schemaWithNewItem.Hp)
        {
            schemaWithNewItem.MaxHp = schemaWithNewItem.Hp;
        }

        return schemaWithNewItem;
    }

    public async Task<OneOf<AppError, None>> EquipBestGatheringEquipment(string skill)
    {
        if (
            !new[]
            {
                PlayerSkill.Alchemy,
                PlayerSkill.Fishing,
                PlayerSkill.Mining,
                PlayerSkill.Woodcutting,
            }.Contains(skill)
        )
        {
            // TODO: Invalid argument, but eh
            return new AppError($"Skill \"{skill}\" is not a valid gathering skill");
        }

        foreach (var item in Character.Schema.Inventory)
        {
            var matchingItemInInventory = GameState.ItemsDict.ContainsKey(item.Code)
                ? GameState.ItemsDict[item.Code]
                : null;

            // Should really never happen that matchingItem is null
            if (
                matchingItemInInventory is not null
                && ItemService.IsEquipment(matchingItemInInventory.Type)
                && matchingItemInInventory.Effects.Find(effect => effect.Code == skill) is not null
            )
            {
                var itemInInventoryEffect = matchingItemInInventory.Effects.Find(effect =>
                    effect.Code == skill
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
                                        .Effects.Find(effect => effect.Code == skill)
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
}
