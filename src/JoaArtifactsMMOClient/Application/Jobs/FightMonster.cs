using Application.ArtifactsApi.Schemas;
using Application.ArtifactsApi.Schemas.Requests;
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

    public const int SECONDS_PER_TURN = 2;
    public const int REST_HP_PERCENTAGE_PER_SEC = 1;

    /**
    ** We assume that obtaining a piece of food takes approx. 35 seconds, since that's roughly what it takes to fish
    ** a fish + cooking. Even when offsetting the cooldown by getting better fishing poles, the difficulty of higher level fish ends up somewhat
    ** evening it out. The value could even be higher, since gathering takes approx. 30 seconds, cooking takes 5 seconds per fish, and moving around
    ** also ends up making it less efficient. But supporting characters will be fishing anyway, since that's also how you obtain pearls, algae,
    ** other materials - and we want the fishing related achievements, so it's not all wated.
    
    ** We should revise this constant occassionally, to find a good balance between eating/resting
    **/
    const int OPPORTUNITY_COST_PER_FOOD_SECONDS = 20;

    // Doesn't matter the amount you consume, cooldown is the same
    private static readonly int COOLDOWN_CONSUMING_FOOD = 3;
    string? ItemCode { get; init; }

    public bool AllowUsingMaterialsFromInventory = false;

    JobMode Mode { get; set; } = JobMode.Kill;

    protected int ProgressAmount { get; set; } = 0;

    bool IsHighPrioMonster { get; set; } = false;

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
        IsHighPrioMonster = GetIsHighPrioMonster(code, gameState);
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

        IsHighPrioMonster = GetIsHighPrioMonster(monsterCode, gameState);
    }

    static bool GetIsHighPrioMonster(string monsterCode, GameState gameState)
    {
        var matchingMonster = gameState.MonstersDict[monsterCode];

        // Not sure if we will keep this boss/raid boss logic here, or the fighting will be a different job
        return matchingMonster.Type == MonsterType.Boss
            || matchingMonster.Type == MonsterType.RaidBoss
            || gameState.Services.EventService.EventEntitiesDict.GetValueOrNull(monsterCode)
                is not null;
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

        if (GetSuitableFoodFromInventory() == 0)
        {
            await Character.QueueJobsBefore(
                Id,
                [
                    new ObtainSuitableFood(
                        Character,
                        gameState,
                        GetFoodAmountToObtain(
                            Character,
                            Mode == JobMode.Kill ? Amount - ProgressAmount : null
                        ),
                        Code
                    ),
                ]
            );
            Status = JobStatus.Suspend;
            return new None();
        }

        int initialAmount =
            Mode == JobMode.Gather ? Character.GetItemFromInventory(ItemCode!)?.Quantity ?? 0 : 0;

        var itemsToEquip = await GetBetterItemsToWithdraw(Character, gameState, monster);

        if (itemsToEquip.Count > 0)
        {
            logger.LogInformation(
                $"{JobName}: [{Character.Schema.Name}] found {itemsToEquip.Count} x jobs to withdraw better items to fight - {string.Join(",", itemsToEquip.Select(item => item.Code).ToList())}"
            );

            foreach (var item in itemsToEquip)
            {
                logger.LogInformation(
                    "{JobName}: [{Character.Schema.Name}] equipping {item.Code} x {item.Quantity} after withdrawing, before fighting",
                    JobName,
                    Character.Schema.Name,
                    item.Code,
                    item.Quantity
                );

                await Character.NavigateTo("bank");

                await Character.WithdrawBankItem(
                    [
                        new WithdrawOrDepositItemRequest
                        {
                            Code = item.Code,
                            Quantity = item.Quantity,
                        },
                    ]
                );

                string snakeCaseSlot = item.Slot.Replace("Slot", "").FromPascalToSnakeCase();

                await Character.EquipItem(
                    new EquipRequest
                    {
                        Code = item.Code,
                        Quantity = item.Quantity,
                        Slot = snakeCaseSlot,
                    }
                );
            }
        }

        await HealIfNotAtFullHp(Character, gameState, IsHighPrioMonster);

        var bankItems = await gameState.Services.BankItemCache.GetBankItems(Character);

        var obtainablePotions = await Character.PlayerActionService.GetObtainablePotions(
            Character,
            gameState
        );

        var availableItems = ItemService.MergeItemEntries(
            ItemService
                .DropSchemaListToItemInInventoryList(bankItems, gameState.ItemsDict)
                .Union(obtainablePotions)
                .ToList()
        );

        var fightSimResult = FightSimulator
            .FindBestFightEquipment(Character, gameState, monster, availableItems)
            .SimResult;

        if (!fightSimResult.Outcome.ShouldFight)
        {
            return new AppError(
                $"Should not fight {Code} - outcome: {fightSimResult.Outcome.Result} - remaining HP would be {fightSimResult.Outcome.PlayerHp}",
                ErrorStatus.InsufficientSkill
            );
        }

        // Figure out if the bank has better fight items, if they have, withdraw them and rerun the job

        var obtainPotionJobs = await HandlePotionsPreFight(monster, fightSimResult);

        if (obtainPotionJobs.Count > 0)
        {
            logger.LogInformation(
                "{JobName}: [{Character.Schema.Name}] obtaining potions before fighting {Code}",
                JobName,
                Character.Schema.Name,
                Code
            );
            await Character.QueueJobsBefore(Id, obtainPotionJobs);
            Status = JobStatus.Suspend;
            return new None();
        }

        var jobsNeededForNavigationResult =
            await Character.PlayerActionService.NavigationService.GetJobsNeededForNavigation(Code);

        if (jobsNeededForNavigationResult.Value is AppError)
        {
            return jobsNeededForNavigationResult.AsT0;
        }

        var jobsNeededForNavigation = jobsNeededForNavigationResult.AsT1;

        if (jobsNeededForNavigation.Count > 0)
        {
            logger.LogInformation(
                "{JobName}: [{Character.Schema.Name}] need to do {count} jobs before we can navigate to {Code}",
                JobName,
                Character.Schema.Name,
                jobsNeededForNavigation.Count,
                Code
            );
            await Character.QueueJobsBefore(Id, jobsNeededForNavigation);
            Status = JobStatus.Suspend;
            return new None();
        }

        await Character.PlayerActionService.EquipBestFightEquipment(monster);

        while (Amount > ProgressAmount)
        {
            if (ShouldInterrupt)
            {
                return new None();
            }

            var result = await InnerJobAsync(monster, fightSimResult);

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
        FightSimResult originalFightSimResult
    )
    {
        logger.LogInformation(
            $"{JobName}: [{Character.Schema.Name}] status for {Character.Schema.Name} - fighting {Code} ({ProgressAmount}/{Amount})"
        );

        if (DepositUnneededItems.ShouldInitDepositItems(Character, false))
        {
            await Character.QueueJobsBefore(
                Id,
                [new DepositUnneededItems(Character, gameState, monster, false)]
            );
            Status = JobStatus.Suspend;
            return new None();
        }

        // Every time the fight routine starts, we just want to make sure he has some food.
        // If he runs out, we want him to gather enough to fight for some time.

        if (GetSuitableFoodFromInventory() == 0)
        {
            await Character.QueueJobsBefore(
                Id,
                [
                    new ObtainSuitableFood(
                        Character,
                        gameState,
                        GetFoodAmountToObtain(
                            Character,
                            Mode == JobMode.Kill ? Amount - ProgressAmount : null
                        ),
                        Code
                    ),
                ]
            );
            Status = JobStatus.Suspend;
            return new None();
        }

        switch (GetActionBeforeFight(Character, gameState, monster))
        {
            case ActionBeforeFight.None:
                break;
            case ActionBeforeFight.AcquirePotions:
            {
                var obtainPotionJobs = await HandlePotionsPreFight(monster, originalFightSimResult);

                if (obtainPotionJobs.Count > 0)
                {
                    await Character.QueueJobsBefore(Id, obtainPotionJobs);
                    Status = JobStatus.Suspend;
                    return new None();
                }
                break;
            }
            case ActionBeforeFight.Heal:
                await HealIfNotAtFullHp(Character, gameState, IsHighPrioMonster);
                break;
        }

        await Character.NavigateTo(Code);

        var result = await Character.Fight();

        if (result.Value is AppError error)
        {
            return error;
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

    private static FoodCandidate? GetFoodToEat(PlayerCharacter character, GameState gameState)
    {
        var relevantFoodItems = gameState.Items.FindAll(item =>
            item.Type == "consumable"
            && item.Level <= character.Schema.Level
            && item.Subtype == "food"
        );
        Dictionary<string, ItemSchema> relevantFoodItemsDict = new();

        foreach (var item in relevantFoodItems)
        {
            relevantFoodItemsDict.Add(item.Code, item);
        }

        List<ItemInInventory> foodInInventory = [];

        foreach (var item in character.Schema.Inventory)
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
            var hpToHeal = character.Schema.MaxHp - character.Schema.Hp;

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
        List<ItemInInventory> foodInInventory =
        [
            .. Character
                .GetItemsFromInventoryWithType("consumable")
                .Where(item => item.Item.Subtype == "food"),
        ];

        int amountOfSuitableFood = 0;

        foreach (var food in foodInInventory)
        {
            if (ItemService.CanUseItem(food.Item, Character.Schema, gameState))
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

        List<UnequipRequest> unequipRequests = [];

        foreach (var utility in utilitySlots)
        {
            var matchingItem = gameState.ItemsDict.GetValueOrNull(utility.ItemCode);

            if (
                matchingItem is not null
                && matchingItem.Effects.Exists(effect => potionEffectsToSkip.Contains(effect.Code))
            )
            {
                int amountToUnequip = Math.Min(
                    Character.GetAvailableInventorySpace() - 5,
                    utility.Amount
                );

                if (amountToUnequip > 0)
                {
                    unequipRequests.Add(
                        new UnequipRequest
                        {
                            Slot = utility.SlotName.FromPascalToSnakeCase(),
                            Quantity = amountToUnequip,
                        }
                    );
                }
            }
        }

        if (unequipRequests.Count > 0)
        {
            await Character.UnequipItems(unequipRequests);
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

        List<EquipRequest> equipRequests = [];

        foreach (var potion in potionsInInventory)
        {
            if (ItemService.CanUseItem(potion.Item, Character.Schema, gameState))
            {
                if (string.IsNullOrEmpty(slot1Equip))
                {
                    slot1Equip = potion.Item.Code;
                    slot1EquipAmount = potion.Quantity;
                    equippedSlot1 = true;
                    equipRequests.Add(
                        new EquipRequest
                        {
                            Code = slot1Equip,
                            Slot = "utility1",
                            Quantity = slot1EquipAmount,
                        }
                    );
                }
                else if (string.IsNullOrEmpty(slot2Equip))
                {
                    slot2Equip = potion.Item.Code;
                    slot2EquipAmount = potion.Quantity;
                    equippedSlot2 = true;
                    equipRequests.Add(
                        new EquipRequest
                        {
                            Code = slot2Equip,
                            Slot = "utility2",
                            Quantity = slot2EquipAmount,
                        }
                    );
                }
                else
                {
                    break;
                }
            }
        }

        if (equipRequests.Count > 0)
        {
            await Character.EquipItems(equipRequests);
        }

        if (!equippedSlot1 || !equippedSlot2)
        {
            // We still have a slot available

            int amountOfPossiblePotionsToConsider = gameState.Items.Count(item =>
                item.Type == "utility"
                && ItemService.CanUseItem(item, Character.Schema, gameState)
                && !item.Effects.Exists(effect => potionEffectsToSkip.Contains(effect.Code))
            );

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
        int maxAmount = character.GetAvailableInventorySpace() / 3;

        if (amountToKill is not null)
        {
            int minAmount = Math.Max(amountToKill.Value, MIN_FOOD_TO_OBTAIN);

            int foodNeededToKillMobs = Math.Min(minAmount, maxAmount);

            return foodNeededToKillMobs;
        }

        return maxAmount;
    }

    public static async Task HealIfNotAtFullHp(
        PlayerCharacter character,
        GameState gameState,
        bool isHighPrioMonster
    )
    {
        if (character.Schema.Hp != character.Schema.MaxHp)
        {
            var bestFoodCandidate = GetFoodToEat(character, gameState);

            if (bestFoodCandidate is not null)
            {
                /**
                ** For each piece of food we eat, we have to remember the opportunity cost of each.
                ** So if we are eating 3 gudgeons to heal up 225 HP, we have to consider that each gudgeon
                ** took time to catch, cook, etc., and if 225 HP is maybe 30% of our total HP, then it's
                ** faster "opportunity wise" to just rest, even if that ends up taking 30 seconds compared to 3 seconds,
                ** in the moment (since eating any quantity of food is 3 seconds). By constantly eating food when it's not efficient,
                ** we also need to constantly obtain new food.
                */

                bool shouldEatFood = false;

                /**
                ** Since cooked meat is something that we acquire "cost free", e.g. we obtain it while killing mobs,
                ** then we might as well use it
                */
                if (
                    isHighPrioMonster
                    || ItemService.IsItemCookedMeat(
                        gameState.ItemsDict[bestFoodCandidate.Code],
                        gameState
                    )
                )
                {
                    shouldEatFood = true;
                }

                if (!shouldEatFood)
                {
                    int opportunityCostForFoodSeconds =
                        OPPORTUNITY_COST_PER_FOOD_SECONDS * bestFoodCandidate.Quantity;

                    int timeToRestSeconds = FightSimulator.GetTimeToRest(
                        character.Schema.MaxHp,
                        character.Schema.Hp
                    );

                    shouldEatFood =
                        opportunityCostForFoodSeconds + COOLDOWN_CONSUMING_FOOD < timeToRestSeconds;
                }

                if (shouldEatFood)
                {
                    await character.UseItem(bestFoodCandidate.Code, bestFoodCandidate.Quantity);
                }
            }

            if (character.Schema.Hp != character.Schema.MaxHp)
            {
                await character.Rest();
            }
        }
    }

    public static async Task<List<EquipmentSlot>> GetBetterItemsToWithdraw(
        PlayerCharacter character,
        GameState gameState,
        MonsterSchema monster
    )
    {
        List<EquipmentSlot> itemsToWithdraw = [];

        var bankItems = await gameState.Services.BankItemCache.GetBankItems(character);

        var obtainablePotions = await character.PlayerActionService.GetObtainablePotions(
            character,
            gameState
        );

        var availableItems = ItemService.MergeItemEntries(
            ItemService
                .DropSchemaListToItemInInventoryList(bankItems, gameState.ItemsDict)
                .Union(obtainablePotions)
                .ToList()
        );

        // var items = bankItems
        //     .Where(item => !string.IsNullOrWhiteSpace(item.Code))
        //     .Select(item => new ItemInInventory
        //     {
        //         Item = gameState.ItemsDict[item.Code],
        //         Quantity = item.Quantity,
        //     })
        //     .ToList();

        foreach (var item in character.Schema.Inventory)
        {
            if (string.IsNullOrWhiteSpace(item.Code))
            {
                continue;
            }

            availableItems.Add(
                new ItemInInventory
                {
                    Item = gameState.ItemsDict[item.Code],
                    Quantity = item.Quantity,
                }
            );
        }

        var result = FightSimulator
            .FindBestFightEquipment(character, gameState, monster, availableItems)
            .SimResult;

        // foreach (var item in result.ItemsToEquip)
        // {
        //     var matchingItem = gameState.ItemsDict[item.Code];

        //     // It's easier for now, we can get into edge cases when withdrawing a lot of potions.
        //     // We also don't care, because AcquirePotionJobs should take care of this
        //     if (matchingItem.Type == "utility")
        //     {
        //         continue;
        //     }

        //     int amountInInventory = character.GetItemFromInventory(item.Code)?.Quantity ?? 0;

        //     int amountInBank =
        //         bankItems.FirstOrDefault(bankItem => bankItem.Code == item.Code)?.Quantity ?? 0;

        //     if (amountInBank > 0 && item.Quantity > amountInInventory)
        //     {
        //         int quantityMissing = item.Quantity - amountInInventory;

        //         if (quantityMissing < 0)
        //         {
        //             quantityMissing = 0;
        //         }

        //         if (quantityMissing > 0)
        //         {
        //             itemsToWithdraw.Add(item with { Quantity = quantityMissing });
        //         }
        //     }
        // }

        return GetItemsToWithdrawFromItemsToEquip(
            character,
            gameState,
            bankItems,
            result.ItemsToEquip,
            true
        );
    }

    public static List<EquipmentSlot> GetItemsToWithdrawFromItemsToEquip(
        PlayerCharacter character,
        GameState gameState,
        List<DropSchema> bankItems,
        List<EquipmentSlot> itemsToEquip,
        bool ignorePotions
    )
    {
        List<EquipmentSlot> itemsToWithdraw = [];

        foreach (var item in itemsToEquip)
        {
            var matchingItem = gameState.ItemsDict[item.Code];

            // It's easier for now, we can get into edge cases when withdrawing a lot of potions.
            // We also don't care, because AcquirePotionJobs should take care of this
            if (ignorePotions && matchingItem.Type == "utility")
            {
                continue;
            }

            int amountInInventory = character.GetItemFromInventory(item.Code)?.Quantity ?? 0;

            int amountInBank =
                bankItems.FirstOrDefault(bankItem => bankItem.Code == item.Code)?.Quantity ?? 0;

            if (amountInBank > 0 && item.Quantity > amountInInventory)
            {
                int quantityMissing = item.Quantity - amountInInventory;

                if (quantityMissing < 0)
                {
                    quantityMissing = 0;
                }

                if (quantityMissing > 0)
                {
                    itemsToWithdraw.Add(item with { Quantity = quantityMissing });
                }
            }
        }

        return itemsToWithdraw;
    }

    public static ActionBeforeFight GetActionBeforeFight(
        PlayerCharacter character,
        GameState gameState,
        MonsterSchema monster
    )
    {
        var fightSimWithCurrentOutcome = FightSimulator.CalculateFightOutcome(
            character.Schema,
            [],
            monster,
            gameState,
            true
        );

        if (!fightSimWithCurrentOutcome.ShouldFight)
        {
            return ActionBeforeFight.AcquirePotions;
        }

        if (character.Schema.Hp == character.Schema.MaxHp)
        {
            return ActionBeforeFight.None;
        }
        if (character.Schema.Hp >= character.Schema.MaxHp * 0.75)
        {
            var schemaWithoutNewPots = character.Schema with { };

            var fightSimAtCurrentHpWithoutPots = FightSimulator.CalculateFightOutcome(
                schemaWithoutNewPots,
                [],
                monster,
                gameState,
                false
            );

            if (
                fightSimAtCurrentHpWithoutPots.ShouldFight
                && fightSimAtCurrentHpWithoutPots.PlayerHp >= character.Schema.MaxHp * 0.40
            )
            {
                return ActionBeforeFight.None;
            }
        }

        return ActionBeforeFight.Heal;
    }
}

public enum ActionBeforeFight
{
    None,
    AcquirePotions,
    Heal,
}

record FoodCandidate
{
    public string Code = "";
    public int Quantity;
    public int TotalHealAmount;
}

public enum JobMode
{
    Kill,
    Gather,
}
