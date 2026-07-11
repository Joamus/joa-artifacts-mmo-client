using Application.ArtifactsApi.Schemas;
using Application.Character;
using Application.Jobs;
using Application.Records;
using Applicaton.Services.FightSimulator;
using OneOf.Types;

namespace Application.Services;

public class FightEquipmentAI
{
    const int ITEM_LEVEL_BUFFER = 5;

    static List<EquipmentTypeMapping> craftableEquipmentTypes { get; } =
        new List<EquipmentTypeMapping>
        {
            new() { ItemType = "weapon", Slot = "WeaponSlot" },
            new() { ItemType = "body_armor", Slot = "BodyArmorSlot" },
            new() { ItemType = "leg_armor", Slot = "LegArmorSlot" },
            new() { ItemType = "helmet", Slot = "HelmetSlot" },
            new() { ItemType = "boots", Slot = "BootsSlot" },
            new() { ItemType = "ring", Slot = "Ring1Slot" },
            new() { ItemType = "ring", Slot = "Ring2Slot" },
            new() { ItemType = "amulet", Slot = "AmuletSlot" },
            new() { ItemType = "shield", Slot = "ShieldSlot" },
        };

    static List<EquipmentTypeMapping> allEquipmentTypes { get; } =
    [
        .. new List<EquipmentTypeMapping>
        {
            new() { ItemType = "artifact", Slot = "Artifact1Slot" },
            new() { ItemType = "artifact", Slot = "Artifact2Slot" },
            new() { ItemType = "artifact", Slot = "Artifact3Slot" },
            new() { ItemType = "rune", Slot = "RuneSlot" },
        }.Union(craftableEquipmentTypes),
    ];

    public static async Task<CharacterJob?> EnsureFightEquipment(
        PlayerCharacter character,
        GameState gameState
    )
    {
        /*
         * Update: Changing the logic so we don't necessarily need to equip this now, but we want to ensure that we
         * have some upgrades available. This update is to remove redundancy, so all characters won't spend time
         * getting a lot of upgrades for their level, and possibly never use them anyway.
         *
         * We need some logic to make sure that the characters' equipment is somewhat up to date.
         * It's difficult to really make this perfect, because higher level equipment isn't necessarily always better,
         * so the ambition should just be to make sure that their items aren't horrible.
         * It's okay if this algorithm isn't perfect, it will still save time.
         * the issue is currently that characters can end up being severely undergeared,
         * because we only make the characters upgrade their items in the "GetNextJobToFightMonster" method,
         * which is only run in some cases. But if a character never gets to actually explicitly run that function,
         * they will often just have "good enough" equipment to fight monsters, and that is inefficient.
         * Especially, because they also end up using a lot of potions and food.
         *
         * Heuristic ideas:
         * - Take an average of all of the item levels the character has equipped - the minimum level should be 10 levels below
         * the character level. If the average is below 10,
         Look at the lowest level items they have equipped in the "normal" equipment slots
         * - There might not be an item
        */

        var equipmentTypes = GetItemSlotsToUpgrade(character, gameState);

        if (equipmentTypes.Count == 0)
        {
            return null;
        }

        var bankItemsDict = (await gameState.BankItemCache.GetBankItems(character)).ToDictionary(
            item => item.Code
        );

        // We basically just want to take the first equipment type, and give one job, to get the best we can of that one
        List<(ItemSchema Item, int DesiredQuantity)> items = [];

        foreach (var (equipmentType, isCraftable) in equipmentTypes)
        {
            // Should also cover artifacts, where each artifact slot is "unique", e.g. can't have 2x perfect_pearl
            int maxAllowedOfItem = equipmentType.ItemType == "ring" ? 2 : 1;

            var equippedItemInSlot = character.GetEquipmentSlot(equipmentType.Slot);
            var equippedItemInSlotLevel = string.IsNullOrWhiteSpace(equippedItemInSlot.Code)
                ? 0
                : gameState.ItemsDict[equippedItemInSlot.Code].Level;

            int itemLevelDiff =
                character.Schema.Level >= ITEM_LEVEL_BUFFER
                    ? ITEM_LEVEL_BUFFER
                    : character.Schema.Level;

            foreach (var item in gameState.Items)
            {
                if (item.Subtype == "tool")
                {
                    continue;
                }

                int quantityOnCharacter = character
                    .GetEquippedItemOrInInventory(item.Code)
                    .Sum((item) => item.equipmentSlot.Quantity);

                // int quantityInBank = bankItemsDict.GetValueOrNull(item.Code)?.Quantity ?? 0;

                // int availableQuantity = quantityOnCharacter + quantityInBank;
                int availableQuantity = quantityOnCharacter;

                if (maxAllowedOfItem <= availableQuantity)
                {
                    continue;
                }

                bool withinLevelRange = equippedItemInSlotLevel <= item.Level + itemLevelDiff;

                bool correctItemType = item.Type == equipmentType.ItemType;

                int desiredQuantity = maxAllowedOfItem - availableQuantity;

                if (
                    correctItemType
                    && withinLevelRange
                    // For now, only craftable items, e.g. don't grind mobs for a certain item
                    && (!isCraftable || item.Craft is not null)
                    && ItemService.CanUseItem(item, character.Schema, gameState)
                    && !character.ExistsInWishlist(item.Code)
                    && await character.PlayerActionService.CanObtainItem(item, 1)
                )
                {
                    items.Add((item, desiredQuantity));
                }
            }
        }

        var relevantItemsFromSim = FightSimulator
            .GetItemsRelevantMonsters(
                character,
                gameState,
                [
                    .. items.Select(item => new ItemInInventory
                    {
                        Item = item.Item,
                        Quantity = item.DesiredQuantity,
                    }),
                ],
                false
            )
            .Where(item =>
            {
                var quantityInBank = bankItemsDict.GetValueOrNull(item)?.Quantity ?? 0;

                /**
                 * The code is needed here, because we need to SIM all available items, and then filter
                 * out the ones that we already have, since we don't need to obtain them if we already have them,
                 * since we can just withdraw when needed (in fight job)
                 *
                 * It should be improved so we actually know how many of the items we will want,
                 * since this implementation might create 2 rings, even if we only need one extra.
                 *It's fine for now.
                */
                var isRing = gameState.ItemsDict[item]?.Type == "ring";

                int probableDesiredAmount = isRing ? 2 : 1;

                return quantityInBank < probableDesiredAmount;
            })
            .ToList();

        relevantItemsFromSim.Sort(
            (a, b) =>
            {
                var itemA = gameState.ItemsDict[a];
                var itemB = gameState.ItemsDict[b];

                int aWinsValue = -1;
                int bWinsValue = 1;

                if (itemB.Craft is null && itemA.Craft is not null)
                {
                    return bWinsValue;
                }
                else if (itemA.Craft is null && itemB.Craft is not null)
                {
                    return aWinsValue;
                }

                if (itemB.Type == "weapon" && itemA.Type != "weapon")
                {
                    return bWinsValue;
                }
                else if (itemA.Type == "weapon" && itemB.Type != "weapon")
                {
                    return aWinsValue;
                }

                return itemB.Level - itemA.Level;
            }
        );

        var highestPriorityItem = relevantItemsFromSim.FirstOrDefault();

        if (highestPriorityItem is not null)
        {
            var job = new ObtainOrFindItem(character, gameState, highestPriorityItem, 1)
            {
                onAfterSuccessEndHook = async () =>
                {
                    await character.SmartItemEquip(highestPriorityItem, 1);
                },
            };

            return job;
        }

        return null;
    }

    public static List<(
        EquipmentTypeMapping equipmentType,
        bool isCraftable
    )> GetItemSlotsToUpgrade(PlayerCharacter character, GameState gameState)
    {
        int minimumItemLevel = Math.Max(character.Schema.Level - ITEM_LEVEL_BUFFER, 0);

        var equipmentTypesToUpgrade = allEquipmentTypes
            .Where(equipmentType =>
            {
                var equippedItemInSlot = character.GetEquipmentSlot(equipmentType.Slot);

                if (
                    equippedItemInSlot is null
                    || string.IsNullOrWhiteSpace(equippedItemInSlot.Code)
                )
                {
                    return true;
                }

                var matchingItem = gameState.ItemsDict[equippedItemInSlot.Code];

                return matchingItem.Level <= minimumItemLevel || matchingItem.Subtype == "tool";
            })
            .Select(equipmentType =>
            {
                bool isCraftable = craftableEquipmentTypes.Exists(craftableType =>
                    equipmentType.ItemType == craftableType.ItemType
                );

                return (equipmentType, isCraftable);
            })
            .ToList();

        return equipmentTypesToUpgrade;
    }
}
