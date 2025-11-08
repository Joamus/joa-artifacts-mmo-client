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

        MonsterSchema? matchingMonster = gameState.Monsters.Find(monster => monster.Code == Code);

        if (matchingMonster is null)
        {
            return new AppError($"Monster with code {Code} could not be found");
        }

        var isPossibleResult = IsPossible(matchingMonster);

        switch (isPossibleResult.Value)
        {
            case AppError jobError:
                return jobError;
        }

        int initialAmount =
            Mode == JobMode.Gather ? Character.GetItemFromInventory(ItemCode!)?.Quantity ?? 0 : 0;

        await Character.PlayerActionService.EquipBestFightEquipment(matchingMonster);

        while (Amount > ProgressAmount)
        {
            if (ShouldInterrupt)
            {
                return new None();
            }

            var result = await InnerJobAsync(matchingMonster);

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

    protected async Task<OneOf<AppError, None>> InnerJobAsync(MonsterSchema monster)
    {
        logger.LogInformation(
            $"{JobName}: [{Character.Schema.Name}] status for {Character.Schema.Name} - fighting {Code} ({ProgressAmount}/{Amount})"
        );

        if (DepositUnneededItems.ShouldInitDepositItems(Character))
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

        var hasNoPotionsEquipped = await EquipPotionsIfNeeded();

        if (hasNoPotionsEquipped)
        {
            var obtainPotionJobs = await ObtainSuitablePotions.GetAcquirePotionJobs(
                Character,
                gameState,
                ObtainSuitablePotions.GetPotionsToObtain(Character),
                monster
            );

            if (obtainPotionJobs.Count > 0)
            {
                Character.QueueJobsBefore(Id, obtainPotionJobs);
                Status = JobStatus.Suspend;
                return new None();
            }
        }

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

        await Character.NavigateTo(Code);

        var result = await Character.Fight();

        if (result.Value is AppError)
        {
            return (AppError)result.Value;
        }
        else if (result.Value is FightResponse)
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

    public OneOf<AppError, None> IsPossible(MonsterSchema monster)
    {
        var fightSimulation = FightSimulator.CalculateFightOutcomeWithBestEquipment(
            Character,
            monster,
            gameState
        );

        if (fightSimulation.ShouldFight)
        {
            return new None();
        }
        else
        {
            return new AppError(
                $"Should not fight {Code} - outcome: {fightSimulation.Result} - remaining HP would be {fightSimulation.PlayerHp}",
                ErrorStatus.InsufficientSkill
            );
        }
    }

    public int GetSuitableFoodFromInventory()
    {
        List<ItemInInventory> foodInInventory = Character.GetItemsFromInventoryWithType(
            "consumable"
        );

        int amountOfSuitableFood = 0;

        foreach (var food in foodInInventory)
        {
            bool isUsuable = Character.Schema.Level >= food.Item.Level;

            if (isUsuable)
            {
                amountOfSuitableFood += food.Quantity;
            }
        }

        return amountOfSuitableFood;
    }

    public async ValueTask<bool> EquipPotionsIfNeeded()
    {
        if (
            Character.Schema.Utility1SlotQuantity <= 5
            || Character.Schema.Utility2SlotQuantity <= 5
        )
        {
            return false;
        }

        string? slot1Equip = null;
        int slot1EquipAmount = 0;

        string? slot2Equip = null;
        int slot2EquipAmount = 0;

        var potionsInInventory = Character.GetItemsFromInventoryWithType("utility");

        foreach (var potion in potionsInInventory)
        {
            if (ItemService.CanUseItem(potion.Item, Character.Schema))
            {
                if (slot1Equip is null)
                {
                    slot1Equip = potion.Item.Code;
                    slot1EquipAmount = potion.Quantity;
                }
                else if (slot2Equip is null)
                {
                    slot2Equip = potion.Item.Code;
                    slot2EquipAmount = potion.Quantity;
                }
                else
                {
                    break;
                }
            }
        }

        bool equippedItem = false;

        if (slot1Equip is not null)
        {
            equippedItem = true;

            await Character.EquipItem(slot1Equip, "utility1", slot1EquipAmount);
        }
        if (slot2Equip is not null)
        {
            equippedItem = true;

            await Character.EquipItem(slot2Equip, "utility2", slot2EquipAmount);
        }

        if (equippedItem)
        {
            return false;
        }

        return true;
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
