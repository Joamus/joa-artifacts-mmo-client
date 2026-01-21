using Application.Artifacts.Schemas;
using Application.ArtifactsApi.Schemas;
using Application.ArtifactsApi.Schemas.Requests;
using Application.Character;
using Application.Errors;
using Application.Records;
using Application.Services;
using Applicaton.Services.FightSimulator;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public class ObtainSuitablePotions : CharacterJob
{
    private readonly int _amount;

    public static readonly int POTION_BATCH_SIZE = 10;
    public static readonly int AMOUNT_OF_TURNS_TO_NOT_USE_PREFIGHT_POTS = 10;

    public MonsterSchema Monster;

    public ObtainSuitablePotions(
        PlayerCharacter playerCharacter,
        GameState gameState,
        int amount,
        MonsterSchema monster
    )
        : base(playerCharacter, gameState)
    {
        _amount = amount;
        Monster = monster;
    }

    protected override async Task<OneOf<AppError, None>> ExecuteAsync()
    {
        logger.LogInformation(
            $"{JobName}: [{Character.Schema.Name}] run started - need to find {_amount} potions"
        );

        // If still not enough, we just go gather and cook some - be biased towards fishing, fastest way to get food

        var jobs = await GetAcquirePotionJobs(
            Character,
            gameState,
            GetPotionsToObtain(Character),
            Monster
        );

        logger.LogInformation(
            $"{JobName}: [{Character.Schema.Name}] found {jobs.Count} jobs - need to find {_amount} potions"
        );
        if (jobs.Count > 0)
        {
            Character.QueueJobsAfter(Id, jobs);
        }

        return new None();
    }

    public static int GetPotionsToObtain(PlayerCharacter character)
    {
        // We want to ensure that we don't fill our inventory
        int inventorySpaceLeft = character.GetInventorySpaceLeft();

        return Math.Min(
            PlayerActionService.MAX_AMOUNT_UTILITY_SLOT,
            (int)Math.Round(inventorySpaceLeft * 0.65)
        );
    }

    public static async Task<List<CharacterJob>> GetAcquirePotionJobs(
        PlayerCharacter character,
        GameState gameState,
        int preferedAmount,
        MonsterSchema monster
    )
    {
        List<(ItemSchema item, bool canCraft, int amountInBank)> potionCandidates = [];

        var bankItemsResponse = await gameState.BankItemCache.GetBankItems(character);

        var potionEffectsToSkip = EffectService.GetPotionEffectsToSkip(character.Schema, monster);

        foreach (var element in gameState.UtilityItemsDict)
        {
            var item = element.Value;

            if (!ItemService.CanUseItem(item, character.Schema))
            {
                continue;
            }

            var canCraftItem =
                item.Craft?.Skill == Skill.Alchemy
                && character.Schema.AlchemyLevel >= item.Craft.Level;

            int amountInBank =
                bankItemsResponse
                    .Data.FirstOrDefault(bankItem => bankItem.Code == item.Code)
                    ?.Quantity
                ?? 0;

            if (!canCraftItem && amountInBank == 0)
            {
                continue;
            }

            if (item.Effects.Exists(effect => potionEffectsToSkip.Contains(effect.Code)))
            {
                continue;
            }

            potionCandidates.Add((item, canCraftItem, amountInBank));
        }

        potionCandidates.Sort(
            // Assuming that higher level pots are better
            (b, a) => a.item.Level - b.item.Level
        // (b, a) =>
        //     ItemService
        //         .GetEffect(a.item, "restore")
        //         .CompareTo(ItemService.GetEffect(b.item, "restore"))
        );

        List<ItemInInventory> potionsForSim = [];

        foreach (var candiate in potionCandidates)
        {
            bool skipCandidate = false;
            foreach (var effect in candiate.item.Effects)
            {
                // Effects cannot overlap (I think)
                if (
                    potionsForSim.Exists(potion =>
                        potion.Item.Effects.Exists(_effect => _effect.Code == effect.Code)
                    )
                )
                {
                    skipCandidate = true;
                    break;
                }
            }

            if (skipCandidate)
            {
                continue;
            }

            potionsForSim.Add(
                new ItemInInventory
                {
                    Item = candiate.item,

                    Quantity = PlayerActionService.MAX_AMOUNT_UTILITY_SLOT,
                }
            );
        }

        var originalSchema = character.Schema with { };

        //
        character.Schema.Utility1Slot = "";
        character.Schema.Utility1SlotQuantity = 0;

        character.Schema.Utility2Slot = "";
        character.Schema.Utility2SlotQuantity = 0;

        var fightSimWithoutPotions = FightSimulator.FindBestFightEquipment(
            character,
            gameState,
            monster,
            []
        );

        // If we can fight without the potions, then don't get new ones
        if (fightSimWithoutPotions.Outcome.ShouldFight)
        {
            // Mutating it back, very important
            character.Schema = originalSchema;

            List<(int Slot, string ItemCode, int Amount)> utilitySlots = [];

            utilitySlots.Add(
                (1, character.Schema.Utility1Slot, character.Schema.Utility1SlotQuantity)
            );
            utilitySlots.Add(
                (2, character.Schema.Utility2Slot, character.Schema.Utility2SlotQuantity)
            );

            foreach (var util in utilitySlots)
            {
                await character.PlayerActionService.DepositPotions(
                    util.Slot,
                    util.ItemCode,
                    util.Amount
                );
            }

            return [];
        }

        /**
        ** Change the logic so we rank all the best potions to use for the character,
        ** and then we find the first potion that is worth using, and we put that one in Util slot 1.
        ** After that, we find the second potion, but our logic should be a bit different.
        ** It's not mandatory for the potion to change the outcome, but if it makes a difference that's big enough, we should use it.
        ** But we should evaluate non-pre fight pots in slot 1, and pre-fight AND non-pre fight pots in slot 2, although we should
        ** not overlap effects in both slots.

        ** The logic should be that we only want to use potions if they change the outcome, else it's a waste and the fight is easy enough.
        ** We would rather eat more food, than use HP pots, because it's more time/resource intensive. But if we are using e.g a HP restore pot,
        ** it should mean that we couldn't reliably defeat the monster without it. If we have already decided this, then we want to minimize the pot usage.

        ** E.g. we are dedicing to use HP pots against a mob, but we are using 10 per fight. At this point, if we can use 1 pre-effect pot (could be dmg boost),
        ** and that potion means that we are only using 7 restore HP pots, then it's economical for us to do. This also assumes that the cost of obtaining
        ** a potion is roughly the same, no matter which one.
        */

        List<(int Slot, string ItemCode, int Amount)> simUtilSlots = [];

        simUtilSlots.Add((1, "", 0));
        simUtilSlots.Add((2, "", 0));

        List<FightSimResult> simsWithPotions = [];

        // We should sim all potion combinations in each slot, but no duplicate pot effects in the slots
        //
        //

        foreach (var potion in potionsForSim)
        {
            foreach (var utilSlot in simUtilSlots) { }
        }

        foreach (var utilSlot in simUtilSlots)
        {
            foreach (var potion in potionsForSim)
            {
                var fightSimWithPotions = FightSimulator.FindBestFightEquipment(
                    character,
                    gameState,
                    monster,
                    new List<ItemInInventory>
                    {
                        new ItemInInventory
                        {
                            Item = potion.Item,
                            Quantity = PlayerActionService.MAX_AMOUNT_UTILITY_SLOT,
                        },
                    }
                );

                bool simpleAvoidPrefightPotions = EffectService.SimpleIsPreFightPotionWorthUsing(
                    fightSimWithPotions
                );

                if (simpleAvoidPrefightPotions)
                {
                    continue;
                }

                simsWithPotions.Add(fightSimWithoutPotions);
            }
        }

        potionCandidates = potionsForSim
            // .Where(potion =>
            // {
            //     // if (!EffectService.IsPreFightPotion(potion.Item))
            //     // {
            //     //     return true;
            //     // }

            //     // character.Schema.Utility1Slot = "";
            //     // character.Schema.Utility1SlotQuantity = 0;

            //     // character.Schema.Utility2Slot = "";
            //     // character.Schema.Utility2SlotQuantity = 0;
            //     var fightSimWithPotions = FightSimulator.FindBestFightEquipment(
            //         character,
            //         gameState,
            //         monster,
            //         new List<ItemInInventory>
            //         {
            //             new ItemInInventory
            //             {
            //                 Item = potion.Item,
            //                 Quantity = PlayerActionService.MAX_AMOUNT_UTILITY_SLOT,
            //             },
            //         }
            //     );

            //     return true;
            // })
            .Select(potion =>
            {
                var candidate = potionCandidates.FirstOrDefault(_candidate =>
                    _candidate.item.Code == potion.Item.Code
                );

                return (item: potion.Item, candidate.canCraft, candidate.amountInBank);
            })
            .ToList();

        potionCandidates.Sort((a, b) => b.item.Level - a.item.Level);

        List<ItemSchema> potionsToAcquire = GetMostEfficientPotionCandidates(
            character.Schema,
            monster,
            gameState,
            potionCandidates.Select(potion => potion.item).ToList()
        );

        // potionCandidates = potionCandidates
        //     .Where(candidate =>
        //     {
        //         // foreach (var candiate in potionCandidates)
        //         // {
        //         bool skipCandidate = false;

        //         foreach (var effect in candidate.item.Effects)
        //         {
        //             // Effects cannot overlap (I think)
        //             if (
        //                 potionCandidates.Exists(potion =>
        //                     potion.item.Effects.Exists(_effect => _effect.Code == effect.Code)
        //                     && potion.item.Code != candidate.item.Code
        //                 )
        //             )
        //             {
        //                 skipCandidate = true;
        //                 break;
        //             }
        //         }

        //         if (skipCandidate)
        //         {
        //             return false;
        //         }

        //         return true;

        //         // potionsForSim.Add(
        //         //     new ItemInInventory
        //         //     {
        //         //         Item = candiate.item,

        //         //         Quantity = PlayerActionService.MAX_AMOUNT_UTILITY_SLOT,
        //         //     }
        //         // );
        //     })
        //     .ToList();

        // Mutating it back, very important
        character.Schema = originalSchema;

        potionCandidates = potionCandidates
            .Where(candidate =>
                potionsToAcquire.Exists(potion => candidate.item.Code == potion.Code)
            )
            .ToList();

        // There should only be two
        if (potionCandidates.Count > 2)
        {
            potionCandidates = potionCandidates.GetRange(0, 2);
        }

        List<CharacterJob> resultJobs = [];

        int amountLeft = preferedAmount;
        // Implement finding the 2 best pots, if any, and equip. Use up stuff from the bank.

        foreach (var potion in potionCandidates)
        {
            var amountInInventory = character.GetItemFromInventory(potion.item.Code)?.Quantity ?? 0;

            amountLeft -= Math.Min(amountInInventory, amountLeft);

            if (potion.amountInBank > 0 && amountLeft > 0)
            {
                var amount = Math.Min(
                    character.GetInventorySpaceLeft() - 1,
                    Math.Min(preferedAmount, potion.amountInBank)
                );

                if (amount > 0)
                {
                    var job = new WithdrawItem(character, gameState, potion.item.Code, amount);

                    amountLeft = amountLeft - amount;
                    resultJobs.Add(job);

                    // Craft it or learn to craft it, if needed.
                    job.CanTriggerObtain = true;
                }
                else
                {
                    break;
                }
            }
        }

        if (amountLeft > 0)
        {
            foreach (var potion in potionCandidates)
            {
                int ingredientsRequiredToCraftOne = 0;

                foreach (var material in potion.item.Craft!.Items)
                {
                    ingredientsRequiredToCraftOne += material.Quantity;
                }

                int totalIngredientsRequired = ingredientsRequiredToCraftOne * amountLeft;

                int iterations = (int)
                    Math.Ceiling((double)totalIngredientsRequired / POTION_BATCH_SIZE);

                for (int i = 0; i < iterations; i++)
                {
                    int amountToCraft = Math.Min(amountLeft, POTION_BATCH_SIZE);

                    if (amountLeft <= 0)
                    {
                        break;
                    }
                    string itemCode = potion.item.Code;

                    var job = new ObtainOrFindItem(character, gameState, itemCode, amountToCraft);
                    job.AllowUsingMaterialsFromBank = true;

                    amountLeft = amountLeft - amountToCraft;
                    resultJobs.Add(job);
                }
            }
            amountLeft = 0;
        }

        foreach (var job in resultJobs)
        {
            job.onSuccessEndHook = async () =>
            {
                int amountInInventory = character.GetItemFromInventory(job.Code)?.Quantity ?? 0;

                character.Logger.LogDebug(
                    $"GetAcquirePotionJobs: [{character.Schema.Name}] onSuccessEndHook: Trying to equip {amountInInventory} x {job.Code}"
                );
                if (amountInInventory > 0)
                {
                    bool availableInUtil1 =
                        character.Schema.Utility1SlotQuantity
                            < PlayerActionService.MAX_AMOUNT_UTILITY_SLOT
                        && (
                            character.Schema.Utility1Slot == ""
                            || character.Schema.Utility1Slot == job.Code
                        );

                    bool availableInUtil2 =
                        character.Schema.Utility2SlotQuantity
                            < PlayerActionService.MAX_AMOUNT_UTILITY_SLOT
                        && (
                            character.Schema.Utility2Slot == ""
                            || character.Schema.Utility2Slot == job.Code
                        );

                    if (availableInUtil1 || availableInUtil2)
                    {
                        character.Logger.LogDebug(
                            $"GetAcquirePotionJobs: [{character.Schema.Name}] onSuccessEndHook: Smart equipping {amountInInventory} x {job.Code}"
                        );
                        await character.SmartItemEquip(job.Code, amountInInventory);
                    }
                    else
                    {
                        character.Logger.LogDebug(
                            $"GetAcquirePotionJobs: [{character.Schema.Name}] onSuccessEndHook: No available util slots for equipping {amountInInventory} x {job.Code} - depositting"
                        );

                        await character.NavigateTo("bank");

                        await character.DepositBankItem(
                            [
                                new WithdrawOrDepositItemRequest
                                {
                                    Code = job.Code,
                                    Quantity = amountInInventory,
                                },
                            ]
                        );
                    }
                }
                else
                {
                    character.Logger.LogDebug(
                        $"GetAcquirePotionJobs: [{character.Schema.Name}] onSuccessEndHook: Found {amountInInventory} x {job.Code} in inventory - not equipping"
                    );
                }
            };
        }

        return resultJobs;
    }

    public static List<ItemSchema> GetMostEfficientPotionCandidates(
        CharacterSchema originalCharacterSchema,
        MonsterSchema monster,
        GameState gameState,
        List<ItemSchema> potions
    )
    {
        int amountOfUtilSlots = 2;

        var characterSchemaClone = originalCharacterSchema with { };
        characterSchemaClone.Utility1Slot = "";
        characterSchemaClone.Utility1SlotQuantity = 0;

        characterSchemaClone.Utility2Slot = "";
        characterSchemaClone.Utility2SlotQuantity = 0;

        List<(FightOutcome Outcome, List<DropSchema> Potions)> fightSimResults = [];

        for (int i = 0; i < amountOfUtilSlots; i++)
        {
            List<DropSchema> simUtilSlots = [];

            simUtilSlots.Add(new DropSchema { Code = "", Quantity = 0 });
            simUtilSlots.Add(new DropSchema { Code = "", Quantity = 0 });

            var iterationClone = characterSchemaClone with { };

            for (int j = 0; j < amountOfUtilSlots; j++)
            {
                foreach (var potion in potions)
                {
                    simUtilSlots[j] = new DropSchema
                    {
                        Code = potion.Code,
                        Quantity = PlayerActionService.MAX_AMOUNT_UTILITY_SLOT,
                    };

                    if (
                        ItemService.ArePotionEffectsOverlapping(
                            gameState,
                            simUtilSlots[0].Code,
                            simUtilSlots[1].Code
                        )
                    )
                    {
                        simUtilSlots[j] = new DropSchema { Code = "", Quantity = 0 };
                        continue;
                    }

                    iterationClone = PlayerActionService.SimulateItemEquip(
                        iterationClone,
                        null,
                        gameState.ItemsDict.GetValueOrNull(simUtilSlots[j].Code)!,
                        $"Utility{j + 1}Slot",
                        simUtilSlots[j].Quantity
                    );

                    var fightSimResult = FightSimulator.CalculateFightOutcome(
                        iterationClone,
                        monster,
                        gameState
                    );

                    fightSimResults.Add(
                        (
                            fightSimResult,
                            simUtilSlots
                                .Select(util => new DropSchema
                                {
                                    Code = util.Code,
                                    Quantity = util.Quantity,
                                })
                                .ToList()
                        )
                    );
                }
            }
        }

        if (fightSimResults.Count == 0)
        {
            return [];
        }

        fightSimResults.Sort(
            (a, b) =>
            {
                int potionDiff = a.Outcome.PotionsUsed - b.Outcome.PotionsUsed;

                if (potionDiff != 0 && a.Outcome.ShouldFight && b.Outcome.ShouldFight)
                {
                    return potionDiff;
                }

                return FightSimulator.CompareSimOutcome(a.Outcome, b.Outcome);
            }
        );

        var bestResult = fightSimResults.ElementAt(0);

        return bestResult
            .Potions.Where(potion => !string.IsNullOrEmpty(potion.Code) && potion.Quantity > 0)
            .Select(potion => gameState.ItemsDict[potion.Code])
            .ToList();
    }
}
