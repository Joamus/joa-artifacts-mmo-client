using System.Linq.Expressions;
using System.Reflection.Metadata.Ecma335;
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
    string? ItemCode { get; init; }

    protected int? Amount { get; set; }

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

    public override async Task<OneOf<AppError, None>> RunAsync()
    {
        // In case of resuming a task
        _shouldInterrupt = false;

        _logger.LogInformation(
            $"{GetType().Name} run started - for {_playerCharacter.Character.Name} - progress {Code} ({ProgressAmount}/{Amount})"
        );

        if (Mode == JobMode.Gather && ItemCode is null)
        {
            return new AppError($"ItemCode cannot be null when JobMode == Gather");
        }

        MonsterSchema? matchingMonster = _gameState.Monsters.Find(monster => monster.Code == Code);

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

        while (Amount > ProgressAmount)
        {
            if (_shouldInterrupt)
            {
                return new None();
            }

            var result = await InnerJobAsync();

            switch (result.Value)
            {
                case AppError jobError:
                    return jobError;
                case JobStatus jobStatus:
                    switch (jobStatus)
                    {
                        case JobStatus.Suspend:
                            return new None();
                        default:
                            throw new Exception("Unhandled case");
                    }
                default:
                    // Just continue
                    break;
            }
            if (Mode == JobMode.Gather)
            {
                int amountInInventory =
                    _playerCharacter.GetItemFromInventory(ItemCode)?.Quantity ?? 0;

                ProgressAmount = amountInInventory;
            }
        }

        _logger.LogInformation(
            $"{GetType().Name} completed for {_playerCharacter.Character.Name} - progress {Code} ({ProgressAmount}/{Amount})"
        );

        return new None();
    }

    protected async Task<OneOf<AppError, None, JobStatus>> InnerJobAsync()
    {
        _logger.LogInformation(
            $"FightJob status for {_playerCharacter.Character.Name} - fighting {Code} ({ProgressAmount}/{Amount})"
        );

        if (DepositUnneededItems.ShouldInitDepositItems(_playerCharacter))
        {
            _playerCharacter.QueueJobsBefore(
                Id,
                [new DepositUnneededItems(_playerCharacter, _gameState)]
            );
            return JobStatus.Suspend;
        }

        // Every time the fight routine starts, we just want to make sure he has some food.
        // If he runs out, we want him to gather enough to fight for some time.

        if (GetSuitableFoodFromInventory() == 0)
        {
            _playerCharacter.QueueJobsBefore(
                Id,
                [
                    new ObtainSuitableFood(
                        _playerCharacter,
                        _gameState,
                        PlayerCharacter.AMOUNT_OF_FOOD_TO_KEEP
                    ),
                ]
            );
            return JobStatus.Suspend;
        }

        if (_playerCharacter.Character.Hp != _playerCharacter.Character.MaxHp)
        {
            var bestFoodCandidate = GetFoodToEat();

            if (bestFoodCandidate is not null)
            {
                await _playerCharacter.UseItem(bestFoodCandidate.Code, bestFoodCandidate.Quantity);

                if (_playerCharacter.Character.Hp != _playerCharacter.Character.MaxHp)
                {
                    await _playerCharacter.Rest();
                }
            }
            else
            {
                await _playerCharacter.Rest();
            }
        }

        await _playerCharacter.NavigateTo(Code, ContentType.Monster);

        var result = await _playerCharacter.Fight();

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

    private void EquipBestItems()
    {
        // If you don't have an item in x slot, then equip it - at some point we should possibly take into consideration to not waste e.g earth dmg pots, if you do no earth dmg
        // You can equip up to 100 utility items per slot, which is nice

        // V2 should include figuring out if the character has better items in their inventory for fighting a particular monster
        // it should run through different combinations, considering the equipment the character has in the inventory, and see if the outcome is better.
        // Maybe it could be performant by caching it in the RunAsync, so we don't consider it every time we fight the mob
    }

    private FoodCandidate? GetFoodToEat()
    {
        var relevantFoodItems = _gameState.Items.FindAll(item =>
            item.Type == "consumable" && item.Level <= _playerCharacter.Character.Level
        );
        Dictionary<string, ItemSchema> relevantFoodItemsDict = new();

        foreach (var item in relevantFoodItems)
        {
            relevantFoodItemsDict.Add(item.Code, item);
        }

        List<ItemInInventory> foodInInventory = [];

        foreach (var item in _playerCharacter.Character.Inventory)
        {
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
        CalculationService.SortFoodBasedOnHealValue(foodInInventory, true);

        // Basically take the last one we looped through
        FoodCandidate? candidate = null;

        foreach (var food in foodInInventory)
        {
            var hpToHeal = _playerCharacter.Character.MaxHp - _playerCharacter.Character.Hp;

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
        var fightSimulation = FightSimulator.CalculateFightOutcome(
            _playerCharacter.Character,
            monster,
            true
        );

        if (fightSimulation.ShouldFight)
        {
            return new None();
        }
        else
        {
            return new AppError(
                $"Should not fight ${Code} - outcome: {fightSimulation.Result} - remaining HP would be {fightSimulation.PlayerHp}",
                ErrorStatus.InsufficientSkill
            );
        }
    }

    public int GetSuitableFoodFromInventory()
    {
        List<ItemInInventory> foodInInventory = _playerCharacter.GetItemsFromInventoryWithType(
            "consumable"
        );

        int amountOfSuitableFood = 0;

        foreach (var food in foodInInventory)
        {
            bool isUsuable = _playerCharacter.Character.Level >= food.Item.Level;

            if (isUsuable)
            {
                amountOfSuitableFood += food.Quantity;
            }
        }

        return amountOfSuitableFood;
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
