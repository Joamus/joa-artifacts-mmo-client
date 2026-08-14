using System.Linq.Expressions;
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

    const float IMPROVEMENT_SCORE_TO_CONSIDER_ITEM = 0.15f;
    const float IMPROVEMENT_SCORE_TO_CONSIDER_ITEM_IF_CAN_EASILY_FIGHT =
        IMPROVEMENT_SCORE_TO_CONSIDER_ITEM * 2;

    const float CAN_EASILY_FIGHT_PLAYER_HP_PERCENTAGE = 0.65f;

    const float IMPROVEMENT_SCORE_MODIFIER_PER_LEVEL = 0.01f;

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
        GameState gameState,
        List<DropSchema> bankItems
    )
    {
        var equipmentTypes = GetItemSlotsToUpgrade(character, gameState);

        if (equipmentTypes.Count == 0)
        {
            return null;
        }

        var bankItemsDict = bankItems.ToDictionary(item => item.Code);

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

        var fightSimsForMonsters = FightSimulator.GetBestFightSimResultsForMonsters(
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
        );

        HashSet<string> relevantItemsFromSimSet = [];

        foreach (var fightSim in fightSimsForMonsters)
        {
            var bestFightItems = fightSim.ItemsToEquip;

            foreach (var item in bestFightItems)
            {
                relevantItemsFromSimSet.Add(item.Code);
            }
        }

        var relevantItemsThatWeAlreadyHave = relevantItemsFromSimSet;

        List<(string Code, bool WeDontHaveItem)> splitRelevantItems =
        [
            .. relevantItemsFromSimSet.Select(item =>
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
                var matchingItem = gameState.ItemsDict[item];

                int probableDesiredAmount = GetAllowedItemAmount(item);

                return (Code: item, WeDontHaveItem: quantityInBank < probableDesiredAmount);
            }),
        ];

        relevantItemsFromSimSet =
        [
            .. splitRelevantItems.Where(item => item.WeDontHaveItem).Select(item => item.Code),
        ];

        relevantItemsThatWeAlreadyHave =
        [
            .. splitRelevantItems.Where(item => !item.WeDontHaveItem).Select(item => item.Code),
        ];

        List<string> relevantItemsFromSim = [.. relevantItemsFromSimSet];

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

        List<ItemImprovement> allImprovements = [];

        foreach (var simWithItem in fightSimsForMonsters)
        {
            // int costWithoutItem = TotalSecondsCostForFight(simWithoutItem);

            var attackingPlayerSchema = simWithItem.Schema;

            foreach (var equipmentSlot in simWithItem.ItemsToEquip)
            {
                /**
                ** This list should contain the items that are relevant to consider, so not
                ** items that we have enough of in the bank
                */
                if (!relevantItemsFromSimSet.Contains(equipmentSlot.Code))
                {
                    continue;
                }

                var item = gameState.ItemsDict[equipmentSlot.Code];

                List<ItemSchema?> otherRelevantItemsWeHaveFromSameSlot =
                [
                    // .. relevantItemsThatWeAlreadyHave
                    .. splitRelevantItems
                        .Select(relevantItem =>
                        {
                            var matchingItem = gameState.ItemsDict[relevantItem.Code];

                            if (matchingItem.Type == item.Type && item.Code != matchingItem.Code)
                            {
                                return matchingItem;
                            }

                            return null;
                        })
                        .OfType<ItemSchema?>()
                        .Where(itemSchema => itemSchema is not null),
                ];

                string slotName = (equipmentSlot.Slot + "_slot").FromSnakeToPascalCase();

                var currentSlot = character.GetEquipmentSlot(slotName);

                var itemInSlotCurrently = !string.IsNullOrWhiteSpace(currentSlot.Code)
                    ? gameState.ItemsDict[currentSlot.Code]
                    : null;

                /**
                ** Hacky, but essentially we only want to sim not having an item in that slot,
                ** if we literally don't have any items in that slot. Else we will always compare a new item,
                ** to not having any item, which is unfair, since any item will be a big improvement then.
                */
                if (
                    itemInSlotCurrently is not null
                    || itemInSlotCurrently is null
                        && !otherRelevantItemsWeHaveFromSameSlot.Exists(otherItem =>
                            otherItem?.Type == item.Type
                        )
                )
                {
                    otherRelevantItemsWeHaveFromSameSlot.Add(itemInSlotCurrently);
                }

                List<ItemImprovement> improvementsForItemComparedToEquivalentItems = [];

                foreach (var otherItem in otherRelevantItemsWeHaveFromSameSlot)
                {
                    var currentItem = string.IsNullOrWhiteSpace(equipmentSlot.Code)
                        ? null
                        : gameState.ItemsDict[equipmentSlot.Code];

                    // Remove the item equipped from the sim, and see the difference
                    var schemaWithoutItem = PlayerActionService.SimulateItemEquip(
                        attackingPlayerSchema,
                        currentItem,
                        otherItem,
                        slotName,
                        1
                    );

                    var simWithoutItem = FightSimulator.CalculateFightOutcome(
                        schemaWithoutItem,
                        [],
                        simWithItem.Outcome.Monster,
                        gameState
                    );

                    var improvementData = new ItemImprovement
                    {
                        FightOutcomeWithoutItem = simWithoutItem,
                        FightOutcomeWithItem = simWithItem.Outcome,
                        Item = item,
                        FractionalImprovement = GetEfficiencyDifference(
                            simWithItem.Outcome,
                            simWithoutItem
                        ),
                        InconvenienceCostForItem = TrainSkill
                            .GetInconvenienceCostCraftItem(item, gameState, bankItems, character)
                            .Score,
                    };

                    improvementsForItemComparedToEquivalentItems.Add(improvementData);
                    // allImprovements.Add(improvementData);
                }

                var mostPessimisticImprovement = improvementsForItemComparedToEquivalentItems
                    .OrderBy(improvement => improvement.FractionalImprovement)
                    .FirstOrDefault();

                if (mostPessimisticImprovement is not null)
                {
                    allImprovements.Add(mostPessimisticImprovement);
                }
            }
        }

        var sortedItemImprovements = SortItemImprovementsRelevantFirst(
            [.. allImprovements.Where(IsItemBigEnoughImprovement)]
        );

        var highestPriorityItem = sortedItemImprovements.FirstOrDefault()?.Item?.Code;

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

    public static string? GetBestNonCombatEffectForResource(
        PlayerCharacter character,
        ResourceSchema resource
    )
    {
        int skilLevel = character.GetSkillLevel(resource.Skill);

        var effect = PlayerActionService.GetBestNonCombatEffectWithLevelDiff(
            skilLevel - resource.Level
        );

        // No reason to get prospecting, if all of the drops have a 100% drop chance
        if (effect == Effect.Prospecting && resource.Drops.All(drop => drop.Rate == 1))
        {
            return null;
        }

        return effect;
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
            var alreadyHasItemEquipped = character.GetEquippedItem(item.Item.Code).Count > 0;

            if (alreadyHasItemEquipped && item.Item.Type == "artifact")
            {
                continue;
            }

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

    public static int FightLengthInSeconds(FightOutcome outcome)
    {
        return Math.Min(FightMonster.SECONDS_PER_TURN * outcome.TotalTurns, 5);
    }

    public static int TotalSecondsCostForFight(FightOutcome outcome)
    {
        int secondsToFight = FightLengthInSeconds(outcome);

        int secondsToRest = outcome.AllPlayerParticipants.Sum(player =>
        {
            int timeToRestSeconds = FightSimulator.GetTimeToRest(
                player.OriginalMaxHp,
                player.Entity.Hp
            );

            return timeToRestSeconds;
        });

        return secondsToFight + secondsToRest;
    }

    public static float GetEfficiencyDifference(
        FightOutcome outcomeWithItem,
        FightOutcome outcomeWithoutItem
    )
    {
        bool bothOutcomesLose =
            outcomeWithItem.Result == FightResult.Loss
            && outcomeWithoutItem.Result == FightResult.Loss;
        /**
        ** The lower the outcome score, the better, since it's the "cost" in seconds. So if the outcome without item is 30 sec,
        ** and the outcome with item is 45, then the score should end up being 1.5, since it's
        ** 1.5 times better.
        */

        int costWithoutItem;
        int costWithItem;

        if (bothOutcomesLose)
        {
            // Essentially if we are losing in either case, we want to consider how long we survive as an improvement
            costWithoutItem = CombatCostIfLosing(outcomeWithoutItem);

            costWithItem = CombatCostIfLosing(outcomeWithItem);
        }
        else
        {
            /**
            ** If equipping this item will result in a switch from losing to winning,
            ** then we should just consider this a 100% improvement - there's not a mathematical
            ** way that we can compare an improvement from losing to winning
            */
            if (
                outcomeWithItem.Result == FightResult.Win
                && outcomeWithoutItem.Result == FightResult.Loss
            )
            {
                return 1;
            }
            costWithoutItem = CombatCostIfWinning(outcomeWithoutItem);

            costWithItem = CombatCostIfWinning(outcomeWithItem);
        }

        return GetEfficiencyDifferenceWithSeconds(costWithItem, costWithoutItem);
    }

    public static int CombatCostIfWinning(FightOutcome outcome)
    {
        int combatLength = FightLengthInSeconds(outcome);

        int secondsToRest = outcome.AllPlayerParticipants.Sum(player =>
        {
            int timeToRestSeconds = FightSimulator.GetTimeToRest(
                player.OriginalMaxHp,
                player.Entity.Hp
            );

            return timeToRestSeconds;
        });

        return combatLength + secondsToRest;
    }

    public static int CombatCostIfLosing(FightOutcome outcome)
    {
        int combatLength = FightLengthInSeconds(outcome);

        float monsterHpLostPercentage = Math.Max(1, (float)outcome.MonsterHp / outcome.Monster.Hp);

        return (int)Math.Round(combatLength * monsterHpLostPercentage);
    }

    public static float GetEfficiencyDifferenceWithSeconds(
        int outcomeWithItemSeconds,
        int outcomeWithoutItemSeconds
    )
    {
        /**
        ** The lower the outcome score, the better, since it's the "cost" in seconds. So if the outcome without item is 30 sec,
        ** and the outcome with item is 45, then the score should end up being 1.5, since it's
        ** 1.5 times better.
        */

        // We want fraction to be e.g. 0.5 for 50%
        float fractionalImprovement =
            (float)(outcomeWithoutItemSeconds - outcomeWithItemSeconds) / outcomeWithItemSeconds;

        // float fractionalImprovement =
        //     1 - ((float)outcomeWithoutItemSeconds / outcomeWithItemSeconds);

        return fractionalImprovement;
    }

    public static List<ItemImprovement> SortItemImprovementsRelevantFirst(
        List<ItemImprovement> itemImprovements
    )
    {
        return
        [
            .. itemImprovements
                .OrderBy((improvement) => improvement.InconvenienceCostForItem)
                .ThenByDescending((improvement) => improvement.FractionalImprovement),
        ];
    }

    public static bool IsItemBigEnoughImprovement(ItemImprovement itemImprovementData)
    {
        var fightOutcomeWithItem = itemImprovementData.FightOutcomeWithItem;
        var fightOutcomeWithoutItem = itemImprovementData.FightOutcomeWithoutItem;

        /**
        ** We have the boss check, to not always buy every upgrade because we will basically
        ** always lose 1-1 with a monster around or level. We still want to consider the upgrades,
        ** but we shouldnt' take them too seriously
        */
        if (
            fightOutcomeWithItem.Monster.Type != MonsterType.Boss
                && fightOutcomeWithItem.ShouldFight
                && !fightOutcomeWithoutItem.ShouldFight
            || fightOutcomeWithItem.Result == FightResult.Win
                && fightOutcomeWithoutItem.Result == FightResult.Loss
        )
        {
            return true;
        }

        int itemLevel = itemImprovementData.Item.Level;

        bool canAlreadyEasilyFight =
            fightOutcomeWithItem.ShouldFight
            && fightOutcomeWithoutItem.ShouldFight
            && fightOutcomeWithoutItem.AllPlayerParticipants.All(player =>
                player.OriginalHp / player.OriginalMaxHp >= CAN_EASILY_FIGHT_PLAYER_HP_PERCENTAGE
            );

        float improvementScoreToConsiderItem = canAlreadyEasilyFight
            ? IMPROVEMENT_SCORE_TO_CONSIDER_ITEM_IF_CAN_EASILY_FIGHT
            : IMPROVEMENT_SCORE_TO_CONSIDER_ITEM;

        /**
        ** The higher level we are, the less picky we should be with improvements,
        ** since each improvement will be relatively worse. e.g the jump from a lvl 1 to 5 weapon is higher than 45 to 50.
        */
        float improvementFactor = itemLevel * IMPROVEMENT_SCORE_MODIFIER_PER_LEVEL;

        float finalFactor = improvementScoreToConsiderItem / (1 + improvementFactor);

        return itemImprovementData.FractionalImprovement >= finalFactor;
    }
}

public record ItemToEquip
{
    public required ItemSchema Item { get; set; }
    public required int Quantity { get; set; }
    public required bool IsInInventory { get; set; }
}

public record ItemImprovement
{
    public required FightOutcome FightOutcomeWithoutItem { get; init; }
    public required FightOutcome FightOutcomeWithItem { get; init; }
    public required ItemSchema Item { get; set; }

    public required float FractionalImprovement { get; set; }
    public required int InconvenienceCostForItem { get; set; }
}
