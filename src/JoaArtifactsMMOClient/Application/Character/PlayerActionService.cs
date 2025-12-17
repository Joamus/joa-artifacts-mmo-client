using System.Numerics;
using Application.Artifacts.Schemas;
using Application.ArtifactsApi.Schemas;
using Application.ArtifactsApi.Schemas.Requests;
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
    public static string SandwhisperIsle = "Sandwhisper Isle";
    public static string ChristmasIsland = "Christmas Island";

    public static List<string> Islands = new List<string> { SandwhisperIsle, ChristmasIsland };

    public static readonly int MAX_AMOUNT_UTILITY_SLOT = 100;
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

    public async Task<bool> CanObtainItem(ItemSchema item)
    {
        var canObtainIt = await ObtainItem.GetJobsRequired(
            character,
            gameState,
            true,
            (await gameState.BankItemCache.GetBankItems(character, false)).Data,
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
        var bankItems = await gameState.BankItemCache.GetBankItems(this.character, false);

        var bankItemDict = new Dictionary<string, DropSchema>();

        foreach (var item in bankItems.Data)
        {
            // Cloning for changing the quantity
            bankItemDict.Add(item.Code, item with { });
        }

        List<ItemSchema> items = [];

        bool itemsAreInWishlist = false;

        foreach (var item in gameState.Items)
        {
            if (character.ExistsInWishlist(item.Code))
            {
                Logger.LogInformation(
                    $"{Name}: [{character.Schema.Name}]: GetIndividualHighPrioJob: Skipping obtaining fight items - {item.Code} is already in wish list, so we should wait until obtaining more"
                );

                itemsAreInWishlist = true;
                continue;
            }

            items.Add(item);
        }

        foreach (var item in character.Schema.Inventory)
        {
            if (string.IsNullOrWhiteSpace(item.Code))
            {
                continue;
            }

            var matchingItem = gameState.ItemsDict[item.Code];

            if (
                matchingItem.Subtype != "tool"
                && ItemService.EquipmentItemTypes.Contains(matchingItem.Type)
            )
            {
                items.Add(matchingItem);
            }
        }

        var bestFightItems = await ItemService.GetBestFightItems(
            character,
            gameState,
            monster,
            items.Select(item => new InventorySlot { Code = item.Code, Quantity = 100 }).ToList()
        );

        List<CharacterJob> jobs = [];

        foreach (var item in bestFightItems)
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

            jobs.Add(new ObtainOrFindItem(character, gameState, item.Code, amountToObtain));
        }

        if (jobs.Count == 0 && itemsAreInWishlist)
        {
            return null;
        }
        return jobs;
    }

    public async void BuyItemFromNpc(string code, int quantity) { }

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

    // public async Task NavigateNextStep(MapSchema destinationMap, string code)
    // {
    //     MapSchema currentMap = gameState.MapsDict[character.Schema.MapId];

    //     bool goingFromIslandToMainland =
    //         Islands.Contains(destinationMap.Name) && !Islands.Contains(currentMap.Name);

    //     bool goingToIslandFromMainland =
    //         Islands.Contains(destinationMap.Name) && !Islands.Contains(currentMap.Name);

    //     bool goingFromIslandToIsland =
    //         Islands.Contains(destinationMap.Name)
    //         && Islands.Contains(currentMap.Name)
    //         && destinationMap.Name != currentMap.Name;

    //     if (goingFromIslandToMainland || goingToIslandFromMainland || goingFromIslandToIsland)
    //     {
    //         if (currentMap.Layer != MapLayer.Overworld)
    //         {
    //             MapSchema? closestTransition = FindClosestTransition(currentMap);

    //             if (closestTransition is null)
    //             {
    //                 throw new Exception($"Cannot find transition, should not happen");
    //             }

    //             await character.Move(closestTransition.X, closestTransition.Y);
    //             return;
    //         }

    //         // Going to Sandwhisper
    //         if (goingToIslandFromMainland)
    //         {
    //             // TODO: Should check if we have enough money etc
    //             await character.Move(-2, 21);
    //             await character.Transition();
    //             await NavigateTo(code);
    //             return;
    //             // We are going to an island
    //         }
    //         else if (goingFromIslandToMainland)
    //         {
    //             // We are going back from an island
    //             // Boat to Sandwhisper Isle
    //             await character.Move(2, 16);
    //             // TODO: Consider using a recall potion if you have one
    //             await character.Transition();
    //             await NavigateTo(code);
    //             return;
    //         }
    //     }

    //     if (destinationMap.Layer != character.Schema.Layer)
    //     {
    //         /*
    //           So we have a few scenarios:
    //           - We are moving from the overworld to underground/interior - in that case, we want to find the transition closest
    //             which leads to the closest coordinate on that layer, to the destination.
    //           - If we are moving from the underground/interior to the overworld, we want to just find the closest transition to
    //             where we are currently.
    //           - We are on a landmass, and have to take a transition to another one, on the same layer.

    //           Normally, we probably are moving from one land, to another landmass, e.g. the "main land" to the Sandwhisper Isle or Christmas Island.
    //           We could technically also be moving from one "cave" on the mainland, to another one.
    //           Worst case scenario, we might be moving from an underground Sandwhisper Isle cell, to an underground Christmas Island cell.

    //           This means that we have to basically be able to handle all transitions and separate steps, with a prioritization system
    //           - We don't want to implement a pathfinding algorithm, because we are lazy. What we would need it for is to know if
    //             we are underground/interior, and have to move to another place underground/interior, but we need to go the overworld first
    //           - We can assume
    //         */

    //         // Find closest transition on our current map

    //         int closestCostToTransition = 0;

    //         MapSchema? closestTransition = null;

    //         foreach (var map in gameState.Maps)
    //         {
    //             if (map.Layer != character.Schema.Layer || map.Interactions.Transition is null)
    //             {
    //                 continue;
    //             }

    //             if (map.Interactions.Transition.Layer != destinationMap.Layer)
    //             {
    //                 continue;
    //             }

    //             int cost = CalculationService.CalculateDistanceToMap(
    //                 character.Schema.X,
    //                 character.Schema.Y,
    //                 destinationMap.X,
    //                 destinationMap.Y
    //             );

    //             if (closestTransition is null || cost < closestCostToTransition)
    //             {
    //                 closestTransition = map;
    //             }
    //         }

    //         if (closestTransition is null)
    //         {
    //             // wtf
    //             // return new AppError(
    //             //     $"Could not find transition to get to {destinationMap.Name} - x = {destinationMap.X} y = {destinationMap.Y}",
    //             //     ErrorStatus.NotFound
    //             // );
    //         }

    //         await character.Move(closestTransition.X, closestTransition.Y);
    //         await character.Transition();
    //         // return await NavigateTo(code);
    //     }

    //     // // Going to Sandwhisper
    //     // if (Islands.Contains(destinationMap.Name) && !Islands.Contains(currentMap.Name))
    //     // {
    //     //     // TODO: Should check if we have enough money etc
    //     //     await Character.Move(-2, 21);
    //     //     await Character.Transition();
    //     //     return await NavigateTo(code);
    //     //     // We are going to Sandwhisper
    //     // }
    //     // else if (!Islands.Contains(destinationMap.Name) && Islands.Contains(currentMap.Name))
    //     // {
    //     //     // We are going back
    //     //     // Boat to Sandwhisper Isle
    //     //     await Character.Move(2, 16);
    //     //     // TODO: Consider using a recall potion if you have one
    //     //     await Character.Transition();
    //     //     return await NavigateTo(code);
    //     // }

    //     await character.Move(destinationMap.X, destinationMap.Y);
    // }

    // public MapSchema? FindClosestTransition(MapSchema currentMap)
    // {
    //     MapSchema? closestTransition = null;
    //     int closestCostToTransition = 0;

    //     foreach (var map in gameState.Maps)
    //     {
    //         if (map.Layer != currentMap.Layer || map.Interactions.Transition is null)
    //         {
    //             continue;
    //         }

    //         if (map.Interactions.Transition.Layer != map.Layer)
    //         {
    //             continue;
    //         }

    //         int cost = CalculationService.CalculateDistanceToMap(
    //             currentMap.X,
    //             currentMap.Y,
    //             map.X,
    //             map.Y
    //         );

    //         if (closestTransition is null || cost < closestCostToTransition)
    //         {
    //             closestTransition = map;
    //             closestCostToTransition = cost;
    //         }
    //     }

    //     return closestTransition;
    // }

    public MapSchema? FindNextTransitionToDestination(
        MapSchema currentMap,
        MapSchema destinationMap
    )
    {
        MapSchema? closestTransition = null;
        int closestCostToTransition = 0;

        foreach (var map in gameState.Maps)
        {
            if (map.Layer != currentMap.Layer || map.Interactions.Transition is null)
            {
                continue;
            }

            if (map.Interactions.Transition.Layer != map.Layer)
            {
                continue;
            }

            /*
             * If we are inside (underground/interior), just find the closest transition.
             * If not, it gets a bit more complicated. If we are going to another island, we need to find the boat.
            */
            if (currentMap.Layer == MapLayer.Overworld) { }

            int cost = CalculationService.CalculateDistanceToMap(
                currentMap.X,
                currentMap.Y,
                map.X,
                map.Y
            );

            if (closestTransition is null || cost < closestCostToTransition)
            {
                closestTransition = map;
                closestCostToTransition = cost;
            }
        }

        return closestTransition;
    }

    public static MapSchema? FindTransitionToUse(List<MapSchema> maps, int x, int y, MapLayer layer)
    {
        MapSchema? closestTransition = null;
        int closestCostToTransition = 0;

        foreach (var map in maps)
        {
            if (map.Layer != layer || map.Interactions.Transition is null)
            {
                continue;
            }

            if (map.Interactions.Transition.Layer != map.Layer)
            {
                continue;
            }

            int cost = CalculationService.CalculateDistanceToMap(x, y, map.X, map.Y);

            if (closestTransition is null || cost < closestCostToTransition)
            {
                closestTransition = map;
                closestCostToTransition = cost;
            }
        }

        return closestTransition;
    }
}
