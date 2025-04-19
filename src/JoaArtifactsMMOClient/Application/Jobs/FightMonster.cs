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
    string? _itemCode { get; init; }

    int? _itemAmount { get; set; }

    protected int _amount { get; set; }

    protected int _progressAmount { get; set; } = 0;

    public FightMonster(PlayerCharacter playerCharacter, string code, int amount)
        : base(playerCharacter)
    {
        _code = code;
        _amount = amount;
    }

    public FightMonster(
        PlayerCharacter playerCharacter,
        string code,
        int amount,
        string itemCode,
        int itemAmount
    )
        : base(playerCharacter)
    {
        _code = code;
        _amount = amount;
        _itemCode = itemCode;
        _itemAmount = itemAmount;
    }

    public override async Task<OneOf<JobError, None>> RunAsync()
    {
        // In case of resuming a task
        _shouldInterrupt = false;

        _logger.LogInformation(
            $"{GetType().Name} run started - for {_playerCharacter._character.Name} - progress {_code} ({_progressAmount}/{_amount})"
        );

        MonsterSchema? matchingMonster = _gameState.Monsters.Find(monster => monster.Code == _code);

        if (matchingMonster is null)
        {
            return new JobError($"Monster with code {_code} could not be found");
        }

        var isPossibleResult = IsPossible(matchingMonster);

        switch (isPossibleResult.Value)
        {
            case JobError jobError:
                return jobError;
        }

        bool isDone = false;

        List<ItemInInventory> foodInInventory = _playerCharacter.GetItemsFromInventoryWithType(
            "consumable"
        );

        int amountOfSuitableFood = 0;

        foreach (var food in foodInInventory)
        {
            bool isUsuable = _playerCharacter._character.Level >= food.Item.Level;

            if (isUsuable)
            {
                amountOfSuitableFood += food.Quantity;
            }
        }

        if (amountOfSuitableFood < PlayerCharacter.AMOUNT_OF_FOOD_TO_KEEP)
        {
            _playerCharacter.QueueJobsBefore(
                Id,
                [new ObtainSuitableFood(_playerCharacter, PlayerCharacter.AMOUNT_OF_FOOD_TO_KEEP)]
            );
            return new None();
            // TODO: Make an ObtainSuitableFood job here, queue it before the current job, and then return
        }
        // if ()

        while (!isDone)
        {
            if (_shouldInterrupt)
            {
                return new None();
            }

            if (DepositUnneededItems.ShouldInitDepositItems(_playerCharacter))
            {
                _playerCharacter.QueueJobsBefore(Id, [new DepositUnneededItems(_playerCharacter)]);
                return new None();
            }

            if (_itemCode is not null && _itemAmount is not null)
            {
                int amountInInventory =
                    _playerCharacter.GetItemFromInventory(_itemCode)?.Quantity ?? 0;

                isDone = amountInInventory >= _itemAmount;
            }
            else
            {
                isDone = _progressAmount >= _amount;
            }

            var result = await InnerJobAsync();

            switch (result.Value)
            {
                case JobError jobError:
                    return jobError;
                default:
                    // Just continue
                    break;
            }
        }

        _logger.LogInformation(
            $"{GetType().Name} completed for {_playerCharacter._character.Name} - progress {_code} ({_progressAmount}/{_amount})"
        );

        return new None();
    }

    protected async Task<OneOf<JobError, None>> InnerJobAsync()
    {
        _logger.LogInformation(
            $"FightJob status for {_playerCharacter._character.Name} - fighting {_code} ({_progressAmount}/{_amount})"
        );

        if (_playerCharacter._character.Hp != _playerCharacter._character.MaxHp)
        {
            var bestFoodCandidate = GetFoodToEat();

            if (bestFoodCandidate is not null)
            {
                await _playerCharacter.UseItem(bestFoodCandidate.Code, bestFoodCandidate.Quantity);

                if (_playerCharacter._character.Hp != _playerCharacter._character.MaxHp)
                {
                    await _playerCharacter.Rest();
                }
            }
            else
            {
                await _playerCharacter.Rest();
            }
        }

        await _playerCharacter.NavigateTo(_code, ContentType.Monster);

        var result = await _playerCharacter.Fight();

        if (result.Value is JobError)
        {
            return (JobError)result.Value;
        }
        else if (result.Value is FightResponse)
        {
            _progressAmount++;
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
            item.Type == "consumable" && item.Level <= _playerCharacter._character.Level
        );
        Dictionary<string, ItemSchema> relevantFoodItemsDict = new();

        foreach (var item in relevantFoodItems)
        {
            relevantFoodItemsDict.Add(item.Code, item);
        }

        List<ItemInInventory> foodInInventory = [];

        foreach (var item in _playerCharacter._character.Inventory)
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

        CalculationService.SortFoodBasedOnHealValue(foodInInventory);

        FoodCandidate? bestFoodCandidate = null;

        foreach (var food in foodInInventory)
        {
            var hpToHeal = _playerCharacter._character.MaxHp - _playerCharacter._character.Hp;

            var healValue = food.Item.Effects.Find(effect => effect.Code == "heal")?.Value ?? 0;

            for (int i = 1; i <= food.Quantity; i++)
            {
                var currentHpHealed = healValue * i;

                bool isBestCandidate = false;

                if (currentHpHealed == hpToHeal)
                {
                    isBestCandidate = true;
                }

                if (!isBestCandidate && currentHpHealed < hpToHeal)
                {
                    if ((healValue / (1 + EAT_FOOD_HP_THRESHOLD)) <= hpToHeal)
                    {
                        isBestCandidate = true;
                    }
                    else if (i - 1 > 0)
                    {
                        // Previous candidate was better, they didn't overshoot as much
                        bestFoodCandidate = new FoodCandidate
                        {
                            Code = food.Item.Code,
                            Quantity = i - 1,
                            TotalHealAmount = currentHpHealed,
                        };
                        break;
                    }
                }

                if (!isBestCandidate && currentHpHealed > hpToHeal)
                {
                    if (healValue * (1 + EAT_FOOD_HP_THRESHOLD) >= hpToHeal)
                    {
                        isBestCandidate = true;
                    }
                }

                if (isBestCandidate)
                {
                    // Only take a better candidate if they heal more
                    if (
                        bestFoodCandidate is null
                        || bestFoodCandidate.TotalHealAmount < currentHpHealed
                    )
                    {
                        bestFoodCandidate = new FoodCandidate
                        {
                            Code = food.Item.Code,
                            Quantity = i,
                            TotalHealAmount = currentHpHealed,
                        };
                    }
                }
            }
        }

        return bestFoodCandidate;
    }

    public OneOf<JobError, None> IsPossible(MonsterSchema monster)
    {
        var fightSimulation = FightSimulatorService.CalculateFightOutcome(
            _playerCharacter._character,
            monster,
            true
        );

        if (fightSimulation.ShouldFight)
        {
            return new None();
        }
        else
        {
            return new JobError(
                $"Should not fight ${_code} - outcome: {fightSimulation.Result} - remaining HP would be {fightSimulation.PlayerHp}",
                JobStatus.InsufficientSkill
            );
        }
    }
}

record FoodCandidate
{
    public string Code = "";
    public int Quantity;
    public int TotalHealAmount;
}
