using Application.ArtifactsApi.Schemas;
using Application.ArtifactsApi.Schemas.Responses;
using Application.Character;
using Application.Errors;
using Application.Records;
using Application.Services;
using Applicaton.Jobs;
using Applicaton.Services.FightSimulator;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public class FightMonster : CharacterJob
{
    private static readonly float EAT_FOOD_HP_THRESHOLD = 0.20f;

    private static readonly int MIN_FOOD_TO_OBTAIN = 20;

    private static readonly int REST_HP_PER_SEC = 5;

    // Doesn't matter the amount you consume, cooldown is the same
    private static readonly int COOLDOWN_CONSUMING_FOOD = 3;
    string? ItemCode { get; init; }

    public bool AllowUsingMaterialsFromInventory = false;

    JobMode Mode { get; set; } = JobMode.Kill;

    protected int ProgressAmount { get; set; } = 0;

    public FightMonster(
        PlayerCharacter playerCharacter,
        GameState gameState,
        string code,
        int amount
    )
        : base(playerCharacter, gameState)
    {
        Code = code;
        Amount = amount;
    }

    public FightMonster(
        PlayerCharacter playerCharacter,
        GameState gameState,
        string monsterCode,
        int amount,
        string itemCode
    )
        : base(playerCharacter, gameState)
    {
        Code = monsterCode;
        Amount = amount; // Amount here is item amount
        ItemCode = itemCode;
        Mode = JobMode.Gather;
    }

    protected override async Task<OneOf<AppError, None>> ExecuteAsync()
    {
        // In case of resuming a task
        ShouldInterrupt = false;

        logger.LogInformation(
            $"{JobName}: [{Character.Schema.Name}] run started - progress {Code} ({ProgressAmount}/{Amount})"
        );

        if (Mode == JobMode.Gather && ItemCode is null)
        {
            return new AppError($"ItemCode cannot be null when JobMode == Gather");
        }

        MonsterSchema? monster = gameState.AvailableMonstersDict.GetValueOrNull(Code);

        if (monster is null)
        {
            return new AppError($"Monster with code {Code} could not be found");
        }

        var fightSimResult = FightSimulator.FindBestFightEquipmentWithUsablePotions(
            Character,
            gameState,
            monster
        );

        if (!fightSimResult.Outcome.ShouldFight)
        {
            return new AppError(
                $"Should not fight {Code} - outcome: {fightSimResult.Outcome.Result} - remaining HP would be {fightSimResult.Outcome.PlayerHp}",
                ErrorStatus.InsufficientSkill
            );
        }

        int initialAmount =
            Mode == JobMode.Gather ? Character.GetItemFromInventory(ItemCode!)?.Quantity ?? 0 : 0;

        await HealIfNotAtFullHp();

        List<CharacterJob> withdrawItemJobs = await GetWithdrawItemJobsIfBetterItemsInBank(
            Character,
            gameState,
            monster
        );

        if (withdrawItemJobs.Count > 0)
        {
            logger.LogInformation(
                $"{JobName}: [{Character.Schema.Name}] found {withdrawItemJobs.Count} x jobs to withdraw better items to fight - {string.Join(",", withdrawItemJobs.Select(item => item.Code).ToList())}"
            );
            Character.QueueJobsBefore(Id, withdrawItemJobs);
            Status = JobStatus.Suspend;
            return new None();
        }

        await Character.PlayerActionService.EquipBestFightEquipment(monster);

        List<ItemInInventory> itemsToEquip = [];

        if (itemsToEquip.Count > 0)
        {
            List<CharacterJob> jobs = [];

            foreach (var item in itemsToEquip)
            {
                jobs.Add(
                    new WithdrawItem(Character, gameState, item.Item.Code, item.Quantity, true)
                );
            }
            Character.QueueJobsBefore(Id, jobs);
            Status = JobStatus.Suspend;
            return new None();
        }

        // Figure out if the bank has better fight items, if they have, withdraw them and rerun the job

        var obtainPotionJobs = await HandlePotionsPreFight(monster, fightSimResult);

        if (obtainPotionJobs.Count > 0)
        {
            Character.QueueJobsBefore(Id, obtainPotionJobs);
            Status = JobStatus.Suspend;
            return new None();
        }

        int potionSlotsUsed = 0;

        if (Character.Schema.Utility1SlotQuantity > 0)
        {
            potionSlotsUsed++;
        }

        if (Character.Schema.Utility2SlotQuantity > 0)
        {
            potionSlotsUsed++;
        }

        while (Amount > ProgressAmount)
        {
            if (ShouldInterrupt)
            {
                return new None();
            }

            var result = await InnerJobAsync(monster, fightSimResult, potionSlotsUsed);

            switch (result.Value)
            {
                case AppError jobError:
                    return jobError;
                default:
                    // Just continue
                    break;
            }

            if (Status == JobStatus.Suspend)
            {
                // Queued other jobs before this job
                return new None();
            }

            if (Mode == JobMode.Gather)
            {
                int amountInInventory = Character.GetItemFromInventory(ItemCode!)?.Quantity ?? 0;

                if (AllowUsingMaterialsFromInventory)
                {
                    ProgressAmount = amountInInventory;
                }
                else
                {
                    ProgressAmount = amountInInventory - initialAmount;
                }
            }
        }

        logger.LogInformation(
            $"{JobName}: [{Character.Schema.Name}] completed - progress {Code} ({ProgressAmount}/{Amount})"
        );

        return new None();
    }

    protected async Task<OneOf<AppError, None>> InnerJobAsync(
        MonsterSchema monster,
        FightSimResult fightSimResult,
        int initialPotionSlotsUsed
    )
    {
        logger.LogInformation(
            $"{JobName}: [{Character.Schema.Name}] status for {Character.Schema.Name} - fighting {Code} ({ProgressAmount}/{Amount})"
        );

        if (DepositUnneededItems.ShouldInitDepositItems(Character, false))
        {
            Character.QueueJobsBefore(
                Id,
                [new DepositUnneededItems(Character, gameState, monster)]
            );
            Status = JobStatus.Suspend;
            return new None();
        }

        // Every time the fight routine starts, we just want to make sure he has some food.
        // If he runs out, we want him to gather enough to fight for some time.

        if (GetSuitableFoodFromInventory() == 0)
        {
            Character.QueueJobsBefore(
                Id,
                [
                    new ObtainSuitableFood(
                        Character,
                        gameState,
                        GetFoodAmountToObtain(
                            Character,
                            Mode == JobMode.Kill ? Amount - ProgressAmount : null
                        )
                    ),
                ]
            );
            Status = JobStatus.Suspend;
            return new None();
        }

        bool hasRunOutOfPotions = false;

        if (initialPotionSlotsUsed > 0)
        {
            if (initialPotionSlotsUsed == 1)
            {
                if (
                    Character.Schema.Utility1SlotQuantity == 0
                    && Character.Schema.Utility2SlotQuantity == 0
                )
                {
                    hasRunOutOfPotions = true;
                }
            }
            else if (initialPotionSlotsUsed == 2)
            {
                if (
                    Character.Schema.Utility1SlotQuantity == 0
                    || Character.Schema.Utility2SlotQuantity == 0
                )
                {
                    hasRunOutOfPotions = true;
                }
            }
        }

        if (hasRunOutOfPotions)
        {
            var obtainPotionJobs = await HandlePotionsPreFight(monster, fightSimResult);

            if (obtainPotionJobs.Count > 0)
            {
                Character.QueueJobsBefore(Id, obtainPotionJobs);
                Status = JobStatus.Suspend;
                return new None();
            }
        }

        if (ShouldHealBeforeFight(Character, gameState, monster))
        {
            await HealIfNotAtFullHp();
        }

        await Character.NavigateTo(Code);

        var result = await Character.Fight();

        if (result.Value is AppError)
        {
            return (AppError)result.Value;
        }
        else if (
            result.Value is FightResponse fightResponse
            && fightResponse.Data.Fight.result == FightResult.Win
        )
        {
            if (Mode == JobMode.Kill)
            {
                ProgressAmount++;
            }
        }

        return new None();
    }

    private FoodCandidate? GetFoodToEat()
    {
        var relevantFoodItems = gameState.Items.FindAll(item =>
            item.Type == "consumable" && item.Level <= Character.Schema.Level
        );
        Dictionary<string, ItemSchema> relevantFoodItemsDict = new();

        foreach (var item in relevantFoodItems)
        {
            relevantFoodItemsDict.Add(item.Code, item);
        }

        List<ItemInInventory> foodInInventory = [];

        foreach (var item in Character.Schema.Inventory)
        {
            if (string.IsNullOrEmpty(item.Code))
            {
                continue;
            }
            var existsInDict = relevantFoodItemsDict.ContainsKey(item.Code);
            if (existsInDict)
            {
                ItemInInventory _item = new ItemInInventory
                {
                    Item = relevantFoodItemsDict[item.Code],
                    Quantity = item.Quantity,
                };
                _item.Quantity = item.Quantity;
                foodInInventory.Add(_item);
            }
        }

        // We want to eat the worst food first, so we clear up our inventory, assuming that we usually have more bad food than good food
        CalculationService.SortItemsBasedOnEffect(foodInInventory, "heal", true);

        // Basically take the last one we looped through
        FoodCandidate? candidate = null;

        foreach (var food in foodInInventory)
        {
            var hpToHeal = Character.Schema.MaxHp - Character.Schema.Hp;

            var foodHealValue = food.Item.Effects.Find(effect => effect.Code == "heal")?.Value ?? 0;

            for (int i = 1; i <= food.Quantity; i++)
            {
                var foodHealWithQuantity = foodHealValue * i;

                // E.g we are going to consume a cooked gudgeon for 75 HP, but we don't even need to recover 32 HP
                // then it's a waste, and we would rather rest

                if (i == 1 && hpToHeal < (foodHealWithQuantity / 2))
                {
                    return null;
                }

                // We might waste a little bit of the food, but that's ok as long as it's not too much
                bool isHealingWithinThresholdOrBelow =
                    foodHealWithQuantity / (1 + EAT_FOOD_HP_THRESHOLD) <= hpToHeal;

                if (foodHealWithQuantity >= hpToHeal && isHealingWithinThresholdOrBelow)
                {
                    return new FoodCandidate
                    {
                        Code = food.Item.Code,
                        Quantity = i,
                        TotalHealAmount = foodHealWithQuantity,
                    };
                }

                bool isHealingMoreThanNeeded = foodHealWithQuantity > hpToHeal;

                if (isHealingMoreThanNeeded)
                {
                    if (i > 1)
                    {
                        int previousFoodHealWithQuantity = foodHealValue * (i - 1);

                        if (
                            previousFoodHealWithQuantity >= hpToHeal
                            || hpToHeal / (1 + EAT_FOOD_HP_THRESHOLD)
                                <= previousFoodHealWithQuantity
                        )
                        {
                            return new FoodCandidate
                            {
                                Code = food.Item.Code,
                                Quantity = i - 1,
                                TotalHealAmount = previousFoodHealWithQuantity,
                            };
                        }
                    }

                    return new FoodCandidate
                    {
                        Code = food.Item.Code,
                        Quantity = i,
                        TotalHealAmount = foodHealWithQuantity,
                    };
                }

                candidate = new FoodCandidate
                {
                    Code = food.Item.Code,
                    Quantity = i,
                    TotalHealAmount = foodHealWithQuantity,
                };
            }

            if (candidate is not null)
            {
                return candidate;
            }
        }

        return null;
    }

    public int GetSuitableFoodFromInventory()
    {
        List<ItemInInventory> foodInInventory = Character.GetItemsFromInventoryWithType(
            "consumable"
        );

        int amountOfSuitableFood = 0;

        foreach (var food in foodInInventory)
        {
            if (ItemService.CanUseItem(food.Item, Character.Schema))
            {
                amountOfSuitableFood += food.Quantity;
            }
        }

        return amountOfSuitableFood;
    }

    public async ValueTask<bool> ShouldGetNewPotionsAndEquipExisting(MonsterSchema monster)
    {
        // Hack, but we assume we are running with preFight = false when running inner sync,
        // and we should already have found the best potions.
        if (
            Character.Schema.Utility1SlotQuantity >= 0
            && Character.Schema.Utility2SlotQuantity >= 0
        )
        {
            return false;
        }

        var potionEffectsToSkip = EffectService.GetPotionEffectsToSkip(Character.Schema, monster);

        var utility1 = (
            "Utility1",
            Character.Schema.Utility1Slot,
            Character.Schema.Utility1SlotQuantity
        );
        var utility2 = (
            "Utility2",
            Character.Schema.Utility2Slot,
            Character.Schema.Utility2SlotQuantity
        );

        List<(string SlotName, string ItemCode, int Amount)> utilitySlots = [];

        utilitySlots.Add(utility1);
        utilitySlots.Add(utility2);

        foreach (var utility in utilitySlots)
        {
            var matchingItem = gameState.ItemsDict.GetValueOrNull(utility.ItemCode);

            if (
                matchingItem is not null
                && matchingItem.Effects.Exists(effect => potionEffectsToSkip.Contains(effect.Code))
            )
            {
                int amountToUnequip = Math.Min(
                    Character.GetInventorySpaceLeft() - 5,
                    utility.Amount
                );

                if (amountToUnequip > 0)
                {
                    await Character.UnequipItem(
                        utility.SlotName.FromPascalToSnakeCase(),
                        amountToUnequip
                    );
                }
            }
        }

        string slot1Equip = Character.Schema.Utility1Slot;
        int slot1EquipAmount = Character.Schema.Utility1SlotQuantity;
        bool equippedSlot1 = false;

        string slot2Equip = Character.Schema.Utility1Slot;
        int slot2EquipAmount = Character.Schema.Utility2SlotQuantity;
        bool equippedSlot2 = false;

        var potionsInInventory = Character
            .GetItemsFromInventoryWithType("utility")
            .Where(potion =>
                !potion.Item.Effects.Exists(effect => potionEffectsToSkip.Contains(effect.Code))
            )
            .ToList();

        foreach (var potion in potionsInInventory)
        {
            if (ItemService.CanUseItem(potion.Item, Character.Schema))
            {
                if (string.IsNullOrEmpty(slot1Equip))
                {
                    slot1Equip = potion.Item.Code;
                    slot1EquipAmount = potion.Quantity;
                    equippedSlot1 = true;
                    await Character.EquipItem(slot1Equip, "utility1", slot1EquipAmount);
                }
                else if (string.IsNullOrEmpty(slot2Equip))
                {
                    slot2Equip = potion.Item.Code;
                    slot2EquipAmount = potion.Quantity;
                    equippedSlot2 = true;
                    await Character.EquipItem(slot2Equip, "utility2", slot2EquipAmount);
                }
                else
                {
                    break;
                }
            }
        }

        if (!equippedSlot1 || !equippedSlot2)
        {
            // We still have a slot available

            int amountOfPossiblePotionsToConsider = gameState
                .Items.Where(item =>
                    item.Type == "utility"
                    && ItemService.CanUseItem(item, Character.Schema)
                    && !item.Effects.Exists(effect => potionEffectsToSkip.Contains(effect.Code))
                )
                .Count();

            int amountUnusedSlots = 0;

            if (!equippedSlot1)
            {
                amountUnusedSlots++;
            }
            if (!equippedSlot2)
            {
                amountUnusedSlots++;
            }

            if (amountUnusedSlots > amountOfPossiblePotionsToConsider)
            {
                return false;
            }
        }

        return true;
    }

    public async Task<List<CharacterJob>> HandlePotionsPreFight(
        MonsterSchema monster,
        FightSimResult fightSimResult
    )
    {
        // var potionEffectsToSkip = EffectService.GetPotionEffectsToSkip(Character.Schema, monster);
        List<string> potionEffectsToSkip = [];

        if (!EffectService.SimpleIsPreFightPotionWorthUsing(fightSimResult))
        {
            foreach (var effect in EffectService.preFightEffects)
            {
                potionEffectsToSkip.Add(effect);
            }
        }

        List<(int Slot, string ItemCode, int Amount)> utilitySlots = [];

        utilitySlots.Add((1, Character.Schema.Utility1Slot, Character.Schema.Utility1SlotQuantity));
        utilitySlots.Add((2, Character.Schema.Utility2Slot, Character.Schema.Utility2SlotQuantity));

        foreach (var utility in utilitySlots)
        {
            var matchingItem = gameState.ItemsDict.GetValueOrNull(utility.ItemCode);

            if (
                matchingItem is not null
                && matchingItem.Effects.Exists(effect => potionEffectsToSkip.Contains(effect.Code))
            )
            {
                await Character.PlayerActionService.DepositPotions(
                    utility.Slot,
                    utility.ItemCode,
                    utility.Amount
                );
            }
        }

        var obtainPotionJobs = await ObtainSuitablePotions.GetAcquirePotionJobs(
            Character,
            gameState,
            ObtainSuitablePotions.GetPotionsToObtain(Character),
            monster
        );

        obtainPotionJobs = obtainPotionJobs
            .Where(job =>
            {
                var potion = gameState.ItemsDict[job.Code];

                return !potion.Effects.Exists(effect => potionEffectsToSkip.Contains(effect.Code));
            })
            .ToList();

        bool samePotions = true;

        List<int> matches = [];

        foreach (var job in obtainPotionJobs)
        {
            bool anyMatches = false;

            foreach (var util in utilitySlots)
            {
                if (util.ItemCode == job.Code)
                {
                    matches.Add(util.Slot);
                    anyMatches = true;
                    break;
                }
            }

            if (!anyMatches)
            {
                samePotions = false;
                break;
            }
        }

        // Maybe it would make sense to restock, but it gets pretty complex
        if (samePotions)
        {
            return [];
        }

        if (!samePotions && obtainPotionJobs.Count > 0)
        {
            utilitySlots = [];

            // This is horrible, but I couldn't be bothered to make it better
            if (!matches.Contains(1))
            {
                utilitySlots.Add(
                    (1, Character.Schema.Utility1Slot, Character.Schema.Utility1SlotQuantity)
                );
            }
            if (!matches.Contains(2))
            {
                utilitySlots.Add(
                    (2, Character.Schema.Utility2Slot, Character.Schema.Utility2SlotQuantity)
                );
            }

            foreach (var util in utilitySlots)
            {
                await Character.PlayerActionService.DepositPotions(
                    util.Slot,
                    util.ItemCode,
                    util.Amount
                );
            }
        }

        return obtainPotionJobs;
    }

    public static int GetFoodAmountToObtain(PlayerCharacter character, int? amountToKill)
    {
        int maxAmount = character.GetInventorySpaceLeft() / 3;

        if (amountToKill is not null)
        {
            int minAmount = Math.Max(amountToKill.Value, MIN_FOOD_TO_OBTAIN);

            int foodNeededToKillMobs = Math.Min(minAmount, maxAmount);

            return foodNeededToKillMobs;
        }

        return maxAmount;
    }

    private async Task HealIfNotAtFullHp()
    {
        if (Character.Schema.Hp != Character.Schema.MaxHp)
        {
            var bestFoodCandidate = GetFoodToEat();

            if (bestFoodCandidate is not null)
            {
                await Character.UseItem(bestFoodCandidate.Code, bestFoodCandidate.Quantity);

                if (Character.Schema.Hp != Character.Schema.MaxHp)
                {
                    await Character.Rest();
                }
            }
            else
            {
                await Character.Rest();
            }
        }
    }

    public static async Task<List<CharacterJob>> GetWithdrawItemJobsIfBetterItemsInBank(
        PlayerCharacter character,
        GameState gameState,
        MonsterSchema monster
    )
    {
        List<CharacterJob> jobs = [];

        var bankResponse = await gameState.BankItemCache.GetBankItems(character);

        var items = bankResponse
            .Data.Select(item => new ItemInInventory
            {
                Item = gameState.ItemsDict[item.Code],
                Quantity = item.Quantity,
            })
            .ToList();

        foreach (var item in character.Schema.Inventory)
        {
            if (string.IsNullOrWhiteSpace(item.Code))
            {
                continue;
            }
            items.Add(
                new ItemInInventory
                {
                    Item = gameState.ItemsDict[item.Code],
                    Quantity = item.Quantity,
                }
            );
        }

        var result = FightSimulator.FindBestFightEquipmentWithUsablePotions(
            character,
            gameState,
            monster,
            items
        );

        foreach (var item in result.ItemsToEquip)
        {
            var matchingItem = gameState.ItemsDict[item.Code];

            // It's easier for now, we can get into edge cases when withdrawing a lot of potions.
            // We also don't care, because AcquirePotionJobs should take care of this
            if (matchingItem.Type == "utility")
            {
                continue;
            }

            int amountInInventory = character.GetItemFromInventory(item.Code)?.Quantity ?? 0;

            int amountInBank =
                bankResponse.Data.FirstOrDefault(bankItem => bankItem.Code == item.Code)?.Quantity
                ?? 0;

            if (amountInBank > 0 && item.Quantity > amountInInventory)
            {
                int quantityMissing = item.Quantity - amountInInventory;

                if (quantityMissing < 0)
                {
                    quantityMissing = 0;
                }

                if (quantityMissing > 0)
                {
                    jobs.Add(
                        new WithdrawItem(character, gameState, item.Code, quantityMissing, false)
                    );
                }
            }
        }

        return jobs;
    }

    public static bool ShouldHealBeforeFight(
        PlayerCharacter character,
        GameState gameState,
        MonsterSchema monster
    )
    {
        if (character.Schema.Hp == character.Schema.MaxHp)
        {
            return false;
        }
        if (character.Schema.Hp >= character.Schema.MaxHp * 0.75)
        {
            var schemaWithoutPots = character.Schema with { };

            var fightSimAtCurrentHpWithoutPots = FightSimulator.CalculateFightOutcome(
                schemaWithoutPots,
                monster,
                gameState,
                false
            );

            if (
                fightSimAtCurrentHpWithoutPots.ShouldFight
                && fightSimAtCurrentHpWithoutPots.PlayerHp >= character.Schema.MaxHp * 0.60
            )
            {
                return false;
            }
        }

        return true;
    }
}

record FoodCandidate
{
    public string Code = "";
    public int Quantity;
    public int TotalHealAmount;
}

enum JobMode
{
    Kill,
    Gather,
}
