using System.Linq.Expressions;
using System.Net;
using Application.Artifacts.Schemas;
using Application.ArtifactsApi.Schemas;
using Application.ArtifactsApi.Schemas.Requests;
using Application.ArtifactsApi.Schemas.Responses;
using Application.Character;
using Application.Errors;
using Application.Jobs;
using Application.Jobs.Orchestrators;
using Application.Records;
using Applicaton.Services.FightSimulator;
using Microsoft.AspNetCore.Mvc;
using OneOf;
using OneOf.Types;

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

    static List<EquipmentTypeMapping>? _equipmentTypesForSim = null;

    public static List<EquipmentTypeMapping> GetEquipmentTypesForSim()
    {
        if (_equipmentTypesForSim is not null)
        {
            return _equipmentTypesForSim;
        }

        _equipmentTypesForSim =
        [
            .. AllEquipmentTypes.OrderBy(type =>
            {
                return type.Slot switch
                {
                    "WeaponSlot" => 1,
                    "BodyArmorSlot" => 2,
                    "LegArmorSlot" => 3,
                    "HelmetSlot" => 4,
                    "BootsSlot" => 5,
                    "Ring1Slot" => 6,
                    "Ring2Slot" => 7,
                    "AmuletSlot" => 8,
                    "ShieldSlot" => 9,
                    "Utility1Slot" => 10,
                    "Utility2Slot" => 11,
                    "Amulet1Slot" => 12,
                    "Amulet2Slot" => 13,
                    "Amulet3Slot" => 14,
                    "Rune" => 15,
                    _ => 20,
                };
            }),
        ];

        return _equipmentTypesForSim;
    }

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

        var obtainablePotions = (
            await character.PlayerActionService.GetObtainablePotions(character, gameState)
        );

        List<ItemInInventory> availableItems = ItemService.MergeItemEntries(
            [
                .. obtainablePotions.Union(
                    bankItems
                        .Where(item => !string.IsNullOrWhiteSpace(item.Code))
                        .Select(item => new ItemInInventory
                        {
                            Item = gameState.ItemsDict[item.Code],
                            Quantity = item.Quantity,
                        })
                ),
            ]
        );

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

            int itemLevelDiffFromEquippedItem =
                character.Schema.Level >= ITEM_LEVEL_BUFFER
                    ? ITEM_LEVEL_BUFFER
                    : character.Schema.Level;

            int itemLevelDiff =
                // character.Schema.Level >= RecycleUnusedItems.RECYCLE_LEVEL_DIFF
                //     ? ITEM_LEVEL_BUFFER
                //     : character.Schema.Level;
                RecycleUnusedItems.RECYCLE_LEVEL_DIFF + ITEM_LEVEL_BUFFER;

            foreach (var item in gameState.Items)
            {
                if (item.Subtype == "tool")
                {
                    continue;
                }
                if (!ItemService.CanUseItem(item, character.Schema, gameState))
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

                // bool withinLevelRange = equippedItemInSlotLevel <= item.Level + itemLevelDiff;
                // No
                bool withinLevelRange =
                    !isCraftable
                    || item.Level + itemLevelDiffFromEquippedItem >= character.Schema.Level
                    || equippedItemInSlotLevel <= item.Level + itemLevelDiff;
                ;

                bool correctItemType = item.Type == equipmentType.ItemType;

                int desiredQuantity = maxAllowedOfItem - availableQuantity;

                if (
                    correctItemType
                    && withinLevelRange
                    // For now, only craftable items, e.g. don't grind mobs for a certain item
                    && (!isCraftable || item.Craft is not null)
                    // && ItemService.CanUseItem(item, character.Schema, gameState)
                    && !character.ExistsInWishlist(item.Code)
                    && await character.PlayerActionService.CanObtainItem(item, 1)
                )
                {
                    items.Add((item, desiredQuantity));
                }
            }
        }
        var relevantMonsters = FightSimulator.GetRelevantMonstersForCharacter(character, gameState);

        List<ItemInInventory> itemPoolForSims = ItemService.MergeItemEntries(
            items
                .Select(item => new ItemInInventory
                {
                    Item = item.Item,
                    Quantity = item.DesiredQuantity,
                })
                .Union(availableItems)
                .ToList()
        );
        // List<ItemInInventory> availableItems = ItemService.MergeItemEntries(
        //     [
        //         .. obtainablePotions.Union(
        //             bankItems
        //                 .Where(item => !string.IsNullOrWhiteSpace(item.Code))
        //                 .Select(item => new ItemInInventory
        //                 {
        //                     Item = gameState.ItemsDict[item.Code],
        //                     Quantity = item.Quantity,
        //                 })
        //         ),
        //     ]
        // );

        var fightSimsForMonsters = FightSimulator.GetBestFightSimResultsForMonsters(
            character,
            gameState,
            // [
            //     .. items.Select(item => new ItemInInventory
            //     {
            //         Item = item.Item,
            //         Quantity = item.DesiredQuantity,
            //     }),
            // ],
            itemPoolForSims,
            false,
            relevantMonsters
        );

        HashSet<string> relevantItemsFromSimSet = [];

        foreach (var fightSim in fightSimsForMonsters)
        {
            var bestFightItems = fightSim.ItemsToEquip;

            foreach (var item in bestFightItems)
            {
                var matchingItem = gameState.ItemsDict[item.Code];

                if (matchingItem.Type != "utility")
                {
                    relevantItemsFromSimSet.Add(item.Code);
                }
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

            var otherCharactersForBossFights = FightBossOrchestrator.GetBestCandidatesToFight(
                character,
                gameState
            );

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
                        InconvenienceCost = TrainSkill.GetInconvenienceCostCraftItem(
                            item,
                            gameState,
                            availableItems,
                            character,
                            otherCharactersForBossFights
                        ),
                    };

                    if (improvementData.FractionalImprovement > 0)
                    {
                        improvementsForItemComparedToEquivalentItems.Add(improvementData);
                    }
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
            [
                .. allImprovements.Where(item =>
                    IsItemBigEnoughImprovement(item) && item.InconvenienceCost.CanObtain
                ),
            ]
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
            skilLevel,
            resource.Level
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

        string res = PlayerActionService.GetBestNonCombatEffectWithLevelDiff(skilLevel, item.Level);

        // Only wisdom works for crafting
        return res == Effect.Wisdom ? Effect.Wisdom : null;
    }

    public static async Task GetAndEquipAvailableItemsWithEffect(
        PlayerCharacter character,
        GameState gameState,
        string effectName
    )
    {
        List<ItemToEquipEnriched> results = await GetItemsToEquipWithEffect(
            character,
            gameState,
            effectName
        );

        foreach (var result in results)
        {
            var item = result.Item;
            var slot = result.Slot;

            var alreadyHasItemEquipped = character.GetEquippedItem(item.Code).Count > 0;

            if (alreadyHasItemEquipped && item.Type == "artifact")
            {
                continue;
            }

            if (!result.IsInInventory)
            {
                await character.NavigateTo(ContentCode.Bank);
                await character.WithdrawBankItem(
                    [
                        new WithdrawOrDepositItemRequest
                        {
                            Code = item.Code,
                            Quantity = result.Quantity,
                        },
                    ]
                );
            }

            var previousItem = character.GetEquipmentSlot(slot.FromSnakeToPascalCase() + "Slot");

            await character.EquipItem(
                new EquipRequest
                {
                    Code = item.Code,
                    Quantity = result.Quantity,
                    Slot = slot,
                }
            );

            if (!string.IsNullOrWhiteSpace(previousItem.Code))
            {
                await character.NavigateTo(ContentCode.Bank);
                await character.DepositBankItem(
                    [new WithdrawOrDepositItemRequest { Code = previousItem.Code, Quantity = 1 }]
                );
            }
        }
    }

    public static async Task<List<ItemToEquipEnriched>> GetItemsToEquipWithEffect(
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

        List<ItemToEquipWithIsInInventory> bankItems =
        [
            .. (await gameState.Services.BankItemCache.GetBankItems(character))
                .Where(IsItemWithEffect)
                .Select(item =>
                {
                    var matchingItem = gameState.ItemsDict[item.Code];

                    return new ItemToEquipWithIsInInventory
                    {
                        Item = matchingItem,
                        Quantity = item.Quantity,
                        IsInInventory = false,
                    };
                }),
        ];

        List<ItemToEquipWithIsInInventory> inventoryItems =
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

                    return new ItemToEquipWithIsInInventory
                    {
                        Item = matchingItem,
                        Quantity = item.Quantity,
                        IsInInventory = true,
                    };
                }),
        ];

        List<ItemToEquipWithIsInInventory> allItems = [.. inventoryItems.Union(bankItems)];

        Dictionary<string, List<ItemToEquipWithIsInInventory>> typeToItemsDict = [];

        foreach (var item in allItems)
        {
            if (
                !typeToItemsDict.TryGetValue(
                    item.Item.Type,
                    out List<ItemToEquipWithIsInInventory>? currentItems
                )
            )
            {
                currentItems = [];
                typeToItemsDict.Add(item.Item.Type, currentItems);
            }

            currentItems.Add(item);
        }

        foreach (var element in typeToItemsDict)
        {
            // Put item with highest effect first in the list
            element.Value.Sort(
                (a, b) =>
                {
                    var aEffect = GetEffectValue(a.Item, effectName);
                    var bEffect = GetEffectValue(b.Item, effectName);

                    return Math.Abs(bEffect) - Math.Abs(aEffect);
                }
            );
        }

        var originalSlots = character
            .GetAllEquipmentSlots()
            .Where(slot =>
                // !new List<string> { "weapon", "utility1", "utility2", "bag" }.Contains(slot.Slot)
                !new List<string> { "utility1", "utility2", "bag" }.Contains(slot.Slot)
            )
            .ToList();

        var slotToEquipmentType = AllEquipmentTypes.ToDictionary(equipmentType =>
            equipmentType.Slot.Replace("Slot", "").FromPascalToSnakeCase()
        );

        var newSlots = character.GetAllEquipmentSlots();

        List<ItemToEquipEnriched> chosenItems =
        [
            .. originalSlots
                .Select(slot =>
                {
                    var equipmentType = slotToEquipmentType[slot.Slot];
                    var isItemSlotForUniqueItems = equipmentType.ItemType == "artifact";
                    var candidates = typeToItemsDict.GetValueOrNull(equipmentType.ItemType) ?? [];
                    var currentItem = gameState.ItemsDict.GetValueOrNull(slot.Code);

                    SimpleEffectSchema? currentEffect = currentItem is null
                        ? null
                        : GetEffect(currentItem, effectName);

                    var bestCandidate = candidates.FirstOrDefault(candidate =>
                    {
                        var candidateEffect = GetEffect(candidate.Item, effectName);

                        if (candidateEffect is null)
                        {
                            return false;
                        }

                        return candidate.Quantity > 0
                            && ItemService.CanUseItem(candidate.Item, character.Schema, gameState)
                            && (
                                currentEffect is null
                                || EffectService.IsFirstEffectBetter(candidateEffect, currentEffect)
                            )
                            && (
                                !isItemSlotForUniqueItems
                                || !ItemIsNotInOtherSlotOrWillBe(candidate.Item.Code, newSlots)
                            );
                    });

                    if (bestCandidate is null)
                    {
                        return null;
                    }

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

                    ItemToEquipEnriched result = new()
                    {
                        Item = bestCandidate.Item,
                        IsInInventory = bestCandidate.IsInInventory,
                        Quantity = amountThatCanBeEquippedInASlot,
                        Slot = slot.Slot,
                    };

                    return result;
                })
                .OfType<ItemToEquipEnriched>()
                .ToList(),
        ];

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

    public static SimpleEffectSchema? GetEffect(ItemSchema item, string effectName)
    {
        return item?.Effects.FirstOrDefault(effect => effect.Code == effectName);
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
                .OrderBy((improvement) => improvement.InconvenienceCost.Cost)
                .ThenByDescending((improvement) => improvement.FractionalImprovement),
        ];
    }

    public static bool IsItemBigEnoughImprovement(ItemImprovement itemImprovementData)
    {
        var fightOutcomeWithItem = itemImprovementData.FightOutcomeWithItem;
        var fightOutcomeWithoutItem = itemImprovementData.FightOutcomeWithoutItem;

        // /**
        // ** We have the boss check, to not always buy every upgrade because we will basically
        // ** always lose 1-1 with a monster around or level. We still want to consider the upgrades,
        // ** but we shouldnt' take them too seriously
        // */
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
                player.Entity.Hp / player.Entity.MaxHp >= CAN_EASILY_FIGHT_PLAYER_HP_PERCENTAGE
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

        return itemImprovementData.FractionalImprovement >= finalFactor
            && itemImprovementData.FractionalImprovement > 0.0f;
    }

    public static async Task<OneOf<AppError, None>> EquipItemsWithHighestEffect(
        PlayerCharacter character,
        GameState gameState,
        string skillName
    )
    {
        var items = character
            .GetItemsFromInventory()
            .Select(item =>
            {
                if (!ItemService.IsEquipment(item.Type))
                {
                    return null;
                }
                // Should really never happen that matchingItem is null
                var matchingEffect = item.Effects.FirstOrDefault(effect =>
                    effect.Code == skillName
                );

                if (matchingEffect is null)
                {
                    return null;
                }

                return new ItemWithMatchingEffect { Item = item, MatchingEffect = matchingEffect };
            })
            .OfType<ItemWithMatchingEffect>()
            .ToList();

        foreach (var item in items)
        {
            var itemInInventory = item.Item;

            var itemInInventoryEffect = item.MatchingEffect;

            var itemSlotsTheItemFits = ItemService.GetItemSlotsFromItemType(itemInInventory.Type);

            // For now we assume that items that can fit in multiple slots, e.g. ring 1, 2, 3, etc won't have these effects.
            // It would be cool to be able to handle it, but it will mean that we have to swap around items potentially, etc.
            // e.g. Ring 1 and 2 both have a bonus to alchemy, then we want to make sure that we only swap out the ring with the
            // lowest bonus, if the ring we are evaluating has higher than any of the other.
            if (
                itemSlotsTheItemFits.Length == 0
                || itemInInventory.Type == "artifact"
                    && ItemService.AreArtifactsOverlapping(item.Item.Code, character.Schema)
            )
            {
                continue;
            }

            var equipmentSlot = character.GetEquipmentSlot(itemSlotsTheItemFits[0]);

            if (equipmentSlot.Code == "")
            {
                await character.SmartItemEquip(itemInInventory.Code);
            }
            else
            {
                var equippedItemInSlot = gameState.ItemsDict.GetValueOrNull(equipmentSlot.Code);

                if (equippedItemInSlot is null)
                {
                    return new AppError(
                        $"Matching item in inventory is null for code \"{equipmentSlot.Code}\""
                    );
                }

                var equippedItemValue = equippedItemInSlot.Effects.Find(effect =>
                    effect.Code == skillName
                );

                // For gathering skills, the lower value, the better, e.g. -10 alchemy means 10% faster gathering
                if (
                    equippedItemValue is null
                    || EffectService.IsFirstEffectBetter(itemInInventoryEffect, equippedItemValue)
                )
                {
                    await character.SmartItemEquip(itemInInventory.Code);
                }
            }
        }

        return new None();
    }
}

public record ItemToEquipWithIsInInventory
{
    public required ItemSchema Item { get; set; }
    public required int Quantity { get; set; }
    public required bool IsInInventory { get; set; }
}

public record ItemToEquipEnriched
{
    public required ItemSchema Item { get; set; }
    public required int Quantity { get; set; }
    public required bool IsInInventory { get; set; }
    public required string Slot { get; set; }
}

public record ItemImprovement
{
    public required FightOutcome FightOutcomeWithoutItem { get; init; }
    public required FightOutcome FightOutcomeWithItem { get; init; }
    public required ItemSchema Item { get; set; }

    public required float FractionalImprovement { get; set; }

    // public required int InconvenienceCostForItem { get; set; }
    public required ItemCostCalculation InconvenienceCost { get; set; }
}

public record ItemWithMatchingEffect
{
    public required ItemSchema Item { get; init; }
    public required SimpleEffectSchema MatchingEffect { get; init; }
}
