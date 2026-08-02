using System.Net;
using Application.ArtifactsApi.Schemas;
using Application.ArtifactsApi.Schemas.Requests;
using Application.ArtifactsApi.Schemas.Responses;
using Application.Character;
using Application.Jobs;
using Application.Records;
using Applicaton.Services.FightSimulator;
using Microsoft.AspNetCore.Mvc;

namespace Application.Services;

public class EquipmentService
{
    const int ITEM_LEVEL_BUFFER = 5;

    public static List<EquipmentTypeMapping> CraftableEquipmentTypes { get; } =
        [
            new() { ItemType = "weapon", Slot = "WeaponSlot" },
            new() { ItemType = "body_armor", Slot = "BodyArmorSlot" },
            new() { ItemType = "leg_armor", Slot = "LegArmorSlot" },
            new() { ItemType = "helmet", Slot = "HelmetSlot" },
            new() { ItemType = "boots", Slot = "BootsSlot" },
            new() { ItemType = "ring", Slot = "Ring1Slot" },
            new() { ItemType = "ring", Slot = "Ring2Slot" },
            new() { ItemType = "amulet", Slot = "AmuletSlot" },
            new() { ItemType = "shield", Slot = "ShieldSlot" },
            new() { ItemType = "utility", Slot = "Utility1Slot" },
            new() { ItemType = "utility", Slot = "Utility2Slot" },
        ];

    public static List<EquipmentTypeMapping> AllEquipmentTypes { get; } =
        [
            .. new List<EquipmentTypeMapping>
            {
                new() { ItemType = "artifact", Slot = "Artifact1Slot" },
                new() { ItemType = "artifact", Slot = "Artifact2Slot" },
                new() { ItemType = "artifact", Slot = "Artifact3Slot" },
                new() { ItemType = "rune", Slot = "RuneSlot" },
            }.Union(CraftableEquipmentTypes),
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

        var bankItemsDict = (
            await gameState.Services.BankItemCache.GetBankItems(character)
        ).ToDictionary(item => item.Code);

        // We basically just want to take the first equipment type, and give one job, to get the best we can of that one
        List<(ItemSchema Item, int DesiredQuantity)> items = [];

        foreach (var (equipmentType, isCraftable) in equipmentTypes)
        {
            // Should also cover artifacts, where each artifact slot is "unique", e.g. can't have 2x perfect_pearl
            int maxAllowedOfItem = GetAllowedItemAmount(equipmentType.ItemType);

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
        var relevantMonsters = FightSimulator.GetRelevantMonstersForCharacter(character, gameState);

        var relevantItemsFromSim = FightSimulator
            .GetItemsRelevantMonstersWithMonsters(
                character,
                gameState,
                [
                    .. items.Select(item => new ItemInInventory
                    {
                        Item = item.Item,
                        Quantity = item.DesiredQuantity,
                    }),
                ],
                false,
                relevantMonsters
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

        /**
        * Important stuff to do:
        * We currently overgear our character, which is way too expensive, e.g. when they level 25, we get them all of the best gear we can,
        * and we keep doing that. This means that we are using a large amount of resources, including precious task items/coins.
        *
        * It would probably be better to treat fight equipment as a serious cost, and instead try to get by with the minimum we can.
        * A balance must be reached, since we don't want to be scraping the bottom, but we don't want to overgear.
        **
        * It's especially important to consider that some item slots are less impactful than others.
        * A scenario that can happen, is that we have a lvl 10 amulet, but want to get a slightly better lvl 20 amulet, even though it
        * gives 10 HP and some wisdom. The issue is that this amulet might require jasper crystals, which is very expensive.
        *
        ** Instead we should try to avoid upgrading gear that's not impactful, until we are forced to. In some cases we might be able to entirely
        * skip certain tiers for some slots, e.g. jump from lvl 10 rings to lvl 25, or might even skip rings for the first 20-25 levels.
        *
        * My current idea is to calculate the difference each item makes in the total fight sim.
        * First we take our sim as our character currently is, allowing them to use items in their inventory.
        * We sim them fighting all of the relevant monsters. Maybe we should save whether there are x% monsters they cannot win against (should fight is false)
        * Then we simulate the character equips the given item, and sim all of the same monsters.
        * We then take the average outcome of each fight, with and without the items, and we compare them.
        *
        * We then calculate % of how much better (or worse it is). This "Improvement" is basically made by comparing the total amount of seconds
        * it would take for the original sim to fight the mob (amount of turns) plus the amount of seconds it would take to rest to full HP.
        * This is basically a sort of seconds-per-full-kill metric, and shows good an overall character does against a monster.
        *
        * We then calculate a score for each result, which is basically the percentage improvement divided by the "inconvenienceCost".
        * This score will tell us the % improvement we get per cost.
        *
        * We do this with all of the items, and sort them by score. If our original result can fight all the mobs, and we are just looking
        * for some improvements to our already good loadout, we should only keep items that have more than a % improvement.
        * This threshold might need to be tested, but a good starting number might be 10%.
        */

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

        var equipmentTypesToUpgrade = AllEquipmentTypes
            .Where(equipmentType =>
            {
                if (equipmentType.ItemType == "utility")
                {
                    return false;
                }
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
                bool isCraftable = CraftableEquipmentTypes.Exists(craftableType =>
                    equipmentType.ItemType == craftableType.ItemType
                );

                return (equipmentType, isCraftable);
            })
            .ToList();

        return equipmentTypesToUpgrade;
    }

    public static string GetBestNonCombatEffectForResource(
        PlayerCharacter character,
        ResourceSchema resource
    )
    {
        int skilLevel = character.GetSkillLevel(resource.Skill);

        return PlayerActionService.GetBestNonCombatEffectWithLevelDiff(skilLevel - resource.Level);
    }

    public static string? GetBestNonCombatEffectForCrafting(
        PlayerCharacter character,
        ItemSchema item
    )
    {
        // Should make better, but OK for now
        int skilLevel = item.Craft is not null ? character.GetSkillLevel(item.Craft.Skill) : 0;

        string res = PlayerActionService.GetBestNonCombatEffectWithLevelDiff(
            skilLevel - item.Level
        );

        // Only wisdom works for crafting
        return res == Effect.Wisdom ? Effect.Wisdom : null;
    }

    public static async Task GetAndEquipAvailableNonCombatItems(
        PlayerCharacter character,
        GameState gameState,
        string effectName
    )
    {
        var items = await GetItemsToEquipWithEffect(character, gameState, effectName);

        foreach (var (item, slot) in items)
        {
            if (!item.IsInInventory)
            {
                await character.NavigateTo("bank");
                await character.WithdrawBankItem(
                    [
                        new WithdrawOrDepositItemRequest
                        {
                            Code = item.Item.Code,
                            Quantity = item.Quantity,
                        },
                    ]
                );
            }

            var previousItem = character.GetEquipmentSlot(slot.FromSnakeToPascalCase() + "Slot");

            await character.EquipItem(
                new EquipRequest
                {
                    Code = item.Item.Code,
                    Quantity = item.Quantity,
                    Slot = slot,
                }
            );

            if (!string.IsNullOrWhiteSpace(previousItem.Code))
            {
                await character.NavigateTo("bank");
                await character.DepositBankItem(
                    [new WithdrawOrDepositItemRequest { Code = previousItem.Code, Quantity = 1 }]
                );
            }
        }
    }

    public static async Task<List<(ItemToEquip item, string Slot)>> GetItemsToEquipWithEffect(
        PlayerCharacter character,
        GameState gameState,
        string effectName
    )
    {
        bool IsItemWithEffect(DropSchema item)
        {
            if (string.IsNullOrWhiteSpace(item.Code))
            {
                return false;
            }

            var matchingItem = gameState.ItemsDict[item.Code];

            return matchingItem.Effects.Exists(effect => effect.Code == effectName);
        }

        List<ItemToEquip> bankItems =
        [
            .. (await gameState.Services.BankItemCache.GetBankItems(character))
                .Where(IsItemWithEffect)
                .Select(item =>
                {
                    var matchingItem = gameState.ItemsDict[item.Code];

                    return new ItemToEquip
                    {
                        Item = matchingItem,
                        Quantity = item.Quantity,
                        IsInInventory = false,
                    };
                }),
        ];

        List<ItemToEquip> inventoryItems =
        [
            .. character
                .Schema.Inventory.Where(
                    (item) =>
                        IsItemWithEffect(
                            new DropSchema { Code = item.Code, Quantity = item.Quantity }
                        )
                )
                .Select(item =>
                {
                    var matchingItem = gameState.ItemsDict[item.Code];

                    return new ItemToEquip
                    {
                        Item = matchingItem,
                        Quantity = item.Quantity,
                        IsInInventory = true,
                    };
                }),
        ];

        List<ItemToEquip> allItems = [.. inventoryItems.Union(bankItems)];

        Dictionary<string, List<ItemToEquip>> typeToItemsDict = [];

        foreach (var item in allItems)
        {
            if (!typeToItemsDict.ContainsKey(item.Item.Type))
            {
                typeToItemsDict.Add(item.Item.Type, []);
            }

            List<ItemToEquip> currentItems = typeToItemsDict[item.Item.Type]!;

            currentItems.Add(item);
        }

        foreach (var element in typeToItemsDict)
        {
            // Put item with highest effect first in teh list
            element.Value.Sort(
                (a, b) =>
                {
                    var aEffect = GetEffectValue(a.Item, effectName);
                    var bEffect = GetEffectValue(b.Item, effectName);

                    return bEffect - aEffect;
                }
            );
        }

        var originalSlots = character
            .GetAllEquipmentSlots()
            .Where(slot =>
                !new List<string> { "weapon", "utility1", "utility2", "bag" }.Contains(slot.Slot)
            )
            .ToList();

        var slotToEquipmentType = AllEquipmentTypes.ToDictionary(equipmentType =>
            equipmentType.Slot.Replace("Slot", "").FromPascalToSnakeCase()
        );

        var newSlots = character.GetAllEquipmentSlots();

        List<(ItemToEquip item, string Slot)> chosenItems = [];

        foreach (var slot in originalSlots)
        {
            var equipmentType = slotToEquipmentType[slot.Slot];

            var isItemSlotForUniqueItems = equipmentType.ItemType == "artifact";

            var candidates = typeToItemsDict.GetValueOrNull(equipmentType.ItemType) ?? [];

            var currentItem = gameState.ItemsDict.GetValueOrNull(slot.Code);

            var currentEffect = currentItem is null ? 0 : GetEffectValue(currentItem, effectName);

            var bestCandidate = candidates.FirstOrDefault(candidate =>
                candidate.Quantity > 0
                && ItemService.CanUseItem(candidate.Item, character.Schema, gameState)
                && GetEffectValue(candidate.Item, effectName) > currentEffect
                && (
                    !isItemSlotForUniqueItems
                    // || ItemIsNotInOtherSlotOrWillBe(candidate.Item.Code, chosenItems, newSlots)
                    || ItemIsNotInOtherSlotOrWillBe(candidate.Item.Code, newSlots)
                )
            );

            if (bestCandidate is not null)
            {
                if (!bestCandidate.IsInInventory)
                {
                    var candidateFromInventory = candidates.FirstOrDefault(candidate =>
                        candidate.Item.Code == bestCandidate.Item.Code
                    );

                    if (candidateFromInventory is not null)
                    {
                        bestCandidate = candidateFromInventory;
                    }
                }

                int amountThatCanBeEquippedInASlot = 1;

                var matchingNewSlot = newSlots.First(newSlot => newSlot.Slot == slot.Slot);

                matchingNewSlot.Code = bestCandidate.Item.Code;
                matchingNewSlot.Quantity = amountThatCanBeEquippedInASlot;

                bestCandidate.Quantity -= amountThatCanBeEquippedInASlot;

                (ItemToEquip item, string Slot) result = (
                    new ItemToEquip
                    {
                        Item = bestCandidate.Item,
                        IsInInventory = bestCandidate.IsInInventory,
                        Quantity = amountThatCanBeEquippedInASlot,
                    },
                    slot.Slot
                );

                chosenItems.Add(result);
            }
        }

        return chosenItems;
    }

    static bool ItemIsNotInOtherSlotOrWillBe(
        string itemCode,
        // List<ItemToEquip> chosenItems,
        List<EquipmentSlot> newSlots
    )
    {
        return !string.IsNullOrWhiteSpace(itemCode)
            && (
                // chosenItems.Exists(item => item.Item.Code == itemCode)
                newSlots.Exists(slot => slot.Code == itemCode)
            );
    }

    public static int GetEffectValue(ItemSchema item, string effectName)
    {
        return item?.Effects.FirstOrDefault(effect => effect.Code == effectName)?.Value ?? 0;
    }

    public static int GetAllowedItemAmount(ItemSchema item)
    {
        return GetAllowedItemAmount(item.Type);
    }

    public static int GetAllowedItemAmount(string itemType)
    {
        return itemType == "ring" ? 2 : 1;
    }
}

public record ItemToEquip
{
    public required ItemSchema Item { get; set; }
    public required int Quantity { get; set; }
    public required bool IsInInventory { get; set; }
    // public required string Slot { get; set; }
}
