using System.Collections.Immutable;
using Application.ArtifactsApi.Schemas;
using Application.Character;
using Application.Jobs;
using Application.Records;
using Applicaton.Services.FightSimulator;

namespace Application.Services;

public class FightEquipmentAI
{
    const int ITEM_LEVEL_BUFFER = 12;

    static List<EquipmentTypeMapping> craftableEquipmentTypes { get; } =
        new List<EquipmentTypeMapping>
        {
            new EquipmentTypeMapping { ItemType = "weapon", Slot = "WeaponSlot" },
            new EquipmentTypeMapping { ItemType = "body_armor", Slot = "BodyArmorSlot" },
            new EquipmentTypeMapping { ItemType = "leg_armor", Slot = "LegArmorSlot" },
            new EquipmentTypeMapping { ItemType = "helmet", Slot = "HelmetSlot" },
            new EquipmentTypeMapping { ItemType = "boots", Slot = "BootsSlot" },
            new EquipmentTypeMapping { ItemType = "ring", Slot = "Ring1Slot" },
            new EquipmentTypeMapping { ItemType = "ring", Slot = "Ring2Slot" },
            new EquipmentTypeMapping { ItemType = "amulet", Slot = "AmuletSlot" },
            new EquipmentTypeMapping { ItemType = "shield", Slot = "ShieldSlot" },
        };

    public static async Task<CharacterJob?> EnsureFightEquipment(
        PlayerCharacter character,
        GameState gameState
    )
    {
        /*
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
         * - If
         *
        */

        var equipmentTypes = GetItemSlotsToUpgrade(character, gameState);

        if (equipmentTypes.Count == 0)
        {
            return null;
        }

        // We basically just want to take the first equipment type, and give one job, to get the best we can of that one
        foreach (var equipmentType in equipmentTypes)
        {
            List<ItemSchema> items = [];

            foreach (var item in gameState.Items)
            {
                if (
                    item.Type == equipmentType.ItemType
                    && !character.ExistsInWishlist(item.Code)
                    && ItemService.CanUseItem(item, character.Schema)
                    // For now, only craftable items, e.g. don't grind mobs for a certain item
                    && item.Craft is not null
                    && await character.PlayerActionService.CanObtainItem(item, 1)
                )
                {
                    items.Add(item);
                }
            }

            var relevantItemsFromSim = FightSimulator
                .GetItemsRelevantMonsters(
                    character,
                    gameState,
                    items
                        .Select(item => new ItemInInventory { Item = item, Quantity = 100 })
                        .ToList(),
                    false
                )
                .ToList();

            relevantItemsFromSim.Sort(
                (a, b) => gameState.ItemsDict[b].Level - gameState.ItemsDict[a].Level
            );

            var highestLevelItem = relevantItemsFromSim.FirstOrDefault();

            if (highestLevelItem is not null)
            {
                var job = new ObtainOrFindItem(character, gameState, highestLevelItem, 1)
                {
                    onAfterSuccessEndHook = async () =>
                    {
                        await character.SmartItemEquip(highestLevelItem, 1);
                    },
                };
            }
        }

        return null;
    }

    public static List<EquipmentTypeMapping> GetItemSlotsToUpgrade(
        PlayerCharacter character,
        GameState gameState
    )
    {
        int minimumItemLevel = Math.Max(character.Schema.Level - ITEM_LEVEL_BUFFER, 0);

        var equipmentTypesToUpgrade = craftableEquipmentTypes
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

                return matchingItem.Level < minimumItemLevel;
            })
            .ToList();

        return equipmentTypesToUpgrade;
    }
}
