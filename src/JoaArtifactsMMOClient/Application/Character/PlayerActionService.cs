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

    private readonly PlayerCharacter PlayerCharacter;

    public PlayerActionService(
        ILogger<PlayerActionService> logger,
        GameState gameState,
        PlayerCharacter character
    )
    {
        Logger = logger;
        GameState = gameState;
        PlayerCharacter = character;
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
                    PlayerCharacter.Schema.X,
                    PlayerCharacter.Schema.Y,
                    map.X,
                    map.Y
                );
                continue;
            }

            int cost = CalculationService.CalculateDistanceToMap(
                PlayerCharacter.Schema.X,
                PlayerCharacter.Schema.Y,
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

        await PlayerCharacter.Move(closestMap.X, closestMap.Y);

        return new None();
    }

    public async Task<OneOf<AppError, None>> SmartItemEquip(string code, int quantity = 1)
    {
        var item = PlayerCharacter.GetItemFromInventory(code);

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
            itemSlot = PlayerCharacter.GetEquipmentSlot(
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

                        await PlayerCharacter.EquipItem(code, equipmentSlot.Slot, amountToEquip);

                        return new None();
                    }

                    // await PlayerCharacter.UnequipItem(equipmentSlot.Slot, equipmentSlot.Quantity);
                }

                // TODO FIX
                await PlayerCharacter.EquipItem(
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

    public async Task<OneOf<AppError, None>> EquipBestFightEquipment(MonsterSchema monster)
    {
        // Save a FightResult with the current equipment
        // Copy the current character schema into a variabe
        // Loop through all equipable items in inventory, and calculate the character schema stats, equipping this item
        // If the result is better than the last one, replace the character schema outside of the loop with this one, write down in a list to equip it, else use the last one.
        // At the end, equip all of the items that are in the list
        //

        if (PlayerCharacter.Schema.WeaponSlot is null)
        {
            return new AppError($"Weapon slot was null - should never happen");
        }

        ItemSchema? bestWeaponCandidate = GameState.ItemsDict.GetValueOrNull(
            PlayerCharacter.Schema.WeaponSlot
        );

        if (bestWeaponCandidate is null)
        {
            return new AppError(
                $"Currently best weapon with code \"{PlayerCharacter.Schema.WeaponSlot}\" is null"
            );
        }

        string initialWeaponCode = bestWeaponCandidate.Code;

        // For now, we only check if we have better weapons

        var weapons = PlayerCharacter.GetItemsFromInventoryWithType("weapon");

        var bestSchemaCandiate = PlayerCharacter.Schema with { };

        var bestFightResult = FightSimulator.CalculateFightOutcome(bestSchemaCandiate, monster);

        foreach (var weapon in weapons)
        {
            ItemSchema? weaponSchema = GameState.ItemsDict.GetValueOrNull(weapon.Item.Code);

            if (weaponSchema is null)
            {
                return new AppError(
                    $"Current weapon with code \"{weapon.Item.Code}\" is null - should never happen"
                );
            }

            if (!ItemService.CanUseItem(weaponSchema, PlayerCharacter.Schema.Level))
            {
                continue;
            }

            var characterSchema = bestSchemaCandiate with { };

            // Subtract the existing effects from the PlayerSchema - then we apply the ones from our hypothetical weapon
            foreach (var effect in bestWeaponCandidate!.Effects)
            {
                var matchingProperty = PlayerCharacter
                    .Schema.GetType()
                    .GetProperty(effect.Code.FromSnakeToPascalCase());

                if (matchingProperty is not null)
                {
                    int currentValue = (int)matchingProperty.GetValue(characterSchema)!;
                    matchingProperty.SetValue(characterSchema, currentValue - effect.Value);
                }
            }

            foreach (var effect in weaponSchema.Effects)
            {
                var matchingProperty = PlayerCharacter
                    .Schema.GetType()
                    .GetProperty(effect.Code.FromSnakeToPascalCase());

                if (matchingProperty is not null)
                {
                    int currentValue = (int)matchingProperty.GetValue(characterSchema)!;
                    matchingProperty.SetValue(characterSchema, currentValue + effect.Value);
                }
            }

            var fightOutcome = FightSimulator.CalculateFightOutcome(characterSchema, monster);

            if (
                fightOutcome.Result == FightResult.Win
                && (
                    bestFightResult.Result != FightResult.Win
                    || fightOutcome.PlayerHp > bestFightResult.PlayerHp
                )
            )
            {
                bestFightResult = fightOutcome;
                bestWeaponCandidate = weapon.Item;
                bestSchemaCandiate = characterSchema;
            }
        }

        if (initialWeaponCode != bestWeaponCandidate.Code)
        {
            Logger.LogInformation(
                $"EquipBestFightEquipment: Swapping \"{initialWeaponCode}\" -> \"{bestWeaponCandidate.Code}\" for {PlayerCharacter.Schema.Name} when fighting \"{monster.Code}\""
            );
            await PlayerCharacter.SmartItemEquip(bestWeaponCandidate.Code);
        }

        return new None();
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

        foreach (var item in PlayerCharacter.Schema.Inventory)
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
                    var equippedItem = PlayerCharacter.GetEquipmentSlot(itemSlotsTheItemFits[0]);

                    switch (equippedItem.Value)
                    {
                        case EquipmentSlot equipmentSlot:
                            if (equipmentSlot.Code == "")
                            {
                                await PlayerCharacter.SmartItemEquip(matchingItemInInventory.Code);
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
                                        $"EquipBestGatheringEquipment: Equipping \"{item.Code}\" instead of \"{equipmentSlot.Code}\" for {PlayerCharacter.Schema.Name} for \"{skill}\""
                                    );

                                    await PlayerCharacter.SmartItemEquip(
                                        matchingItemInInventory.Code
                                    );
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
            var thisSlot = PlayerCharacter.GetEquipmentSlot(slot);

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
