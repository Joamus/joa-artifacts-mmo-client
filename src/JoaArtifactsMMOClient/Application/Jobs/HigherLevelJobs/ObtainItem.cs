using Application;
using Application.ArtifactsApi.Schemas;
using Application.ArtifactsApi.Schemas.Responses;
using Application.Character;
using Application.Errors;
using Application.Jobs;
using Application.Services;
using Application.Services.ApiServices;
using Applicaton.Jobs;
using Applicaton.Services.FightSimulator;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public class ObtainItem : CharacterJob
{
    private bool _useItemIfInInventory { get; set; } = false;

    private bool _allowTakingFromBank { get; set; } = true;

    private List<DropSchema> itemsInBank { get; set; } = [];
    protected string _code { get; init; }
    protected int _amount { get; init; }

    protected int _progressAmount { get; set; } = 0;

    public ObtainItem(PlayerCharacter playerCharacter, GameState gameState, string code, int amount)
        : base(playerCharacter, gameState)
    {
        _code = code;
        _amount = amount;
    }

    public ObtainItem(
        PlayerCharacter playerCharacter,
        GameState gameState,
        string code,
        int amount,
        bool useItemIfInInventory,
        bool allowTakingFromBank = true
    )
        : base(playerCharacter, gameState)
    {
        _code = code;
        _amount = amount;
        _useItemIfInInventory = useItemIfInInventory;
        _allowTakingFromBank = allowTakingFromBank;
    }

    public override async Task<OneOf<AppError, None>> RunAsync()
    {
        // It's not very elegant that this job is pasted in multiple places, but a lot of jobs want to have their inventory be clean before they start, or in their InnerJob.
        if (DepositUnneededItems.ShouldInitDepositItems(_playerCharacter))
        {
            _playerCharacter.QueueJobsBefore(
                Id,
                [new DepositUnneededItems(_playerCharacter, _gameState)]
            );
            return new None();
        }

        List<CharacterJob> jobs = [];
        _logger.LogInformation(
            $"{GetType().Name} run started - for {_playerCharacter.Character.Name} - progress {_code} ({_progressAmount}/{_amount})"
        );

        if (_allowTakingFromBank)
        {
            var accountRequester = GameServiceProvider
                .GetInstance()
                .GetService<AccountRequester>()!;

            var bankResult = await accountRequester.GetBankItems();

            if (bankResult is not BankItemsResponse bankItemsResponse)
            {
                return new AppError("Failed to get bank items");
            }

            itemsInBank = bankItemsResponse.Data;
        }
        // useItemIfInInventory is set to the job's value at first, so we can allow obtaining an item we already have.
        // But if we have the ingredients in our inventory, then we should always use them (for now).
        // Having this variable will allow us to e.g craft multiple copper daggers, else we could only have 1 in our inventory

        var result = await GetJobsRequired(jobs, _code, _amount, _useItemIfInInventory);

        _logger.LogInformation(
            $"{GetType().Name} - found {jobs.Count} jobs to run, to obtain item {_code} for {_playerCharacter.Character.Name}"
        );

        switch (result.Value)
        {
            case AppError jobError:
                return jobError;
        }

        _playerCharacter.QueueJobsAfter(Id, jobs);

        return new None();
    }

    /**
     * Get all the jobs required to obtain an item
     * We mutate a list to recursively add all the required jobs to the list
    */
    public async Task<OneOf<AppError, None>> GetJobsRequired(
        List<CharacterJob> jobs,
        string code,
        int amount,
        bool useItemIfInInventory
    )
    {
        var matchingItem = _gameState.Items.Find(item => item.Code == code);

        if (matchingItem is null)
        {
            return new AppError($"Could not find item with code {code} - could not gather it");
        }

        // We have the item already, no need to get it again

        int amountInInventory = useItemIfInInventory
            ? (_playerCharacter.GetItemFromInventory(code)?.Quantity ?? 0)
            : 0;

        if (useItemIfInInventory && amountInInventory >= amount)
        {
            return new None();
        }

        if (_allowTakingFromBank)
        {
            var matchingItemInBank = itemsInBank.FirstOrDefault(item => item.Code == code);
            int amountInBank = matchingItemInBank?.Quantity ?? 0;

            int amountToTakeFromBank = Math.Min(amountInBank, amount);

            if (amountToTakeFromBank > 0)
            {
                jobs.Add(new CollectItem(_playerCharacter, _gameState, code, amountToTakeFromBank));

                amount -= amountToTakeFromBank;
            }
        }

        int requiredAmount = amount - amountInInventory;

        if (requiredAmount <= 0)
        {
            return new None();
        }

        if (matchingItem.Craft is not null)
        {
            foreach (var item in matchingItem.Craft.Items)
            {
                var result = await GetJobsRequired(
                    jobs,
                    item.Code,
                    item.Quantity * requiredAmount,
                    true
                );

                switch (result.Value)
                {
                    case AppError jobError:
                        return jobError;
                }
            }
            jobs.Add(new CraftItem(_playerCharacter, _gameState, code, requiredAmount));
        }
        else
        {
            List<ResourceSchema> resources = _gameState.Resources.FindAll(resource =>
                resource.Drops.Find(drop => drop.Code == code && drop.Rate > 0) != null
            );

            if (resources.Count > 0)
            {
                jobs.Add(new GatherResource(_playerCharacter, _gameState, code, requiredAmount));
            }
            else
            {
                if (matchingItem.Subtype == "task")
                {
                    CharacterJob? monsterTask = null;
                    // Pick up a task, or complete one you have
                    if (_playerCharacter.Character.TaskType == "monsters")
                    {
                        var monster = _gameState.Monsters.Find(monster =>
                            monster.Drops.Find(drop => drop.Code == code) is not null
                        );
                        if (monster is null)
                        {
                            return new AppError(
                                $"Monster with code {code} not found",
                                ErrorStatus.NotFound
                            );
                        }

                        if (
                            FightSimulator
                                .CalculateFightOutcome(_playerCharacter.Character, monster)
                                .ShouldFight
                        )
                        {
                            jobs.Add(new MonsterTask(_playerCharacter, _gameState));
                        }
                        else
                        {
                            return new AppError(
                                $"You cannot obtain item with code {code}, because you need to complete your mosnter task, and you cannot beat the monster",
                                ErrorStatus.InsufficientSkill
                            );
                        }
                    }
                    else
                    {
                        jobs.Add(new ItemTask(_playerCharacter, _gameState));
                    }
                }
                else
                {
                    List<MonsterSchema> suitableMonsters = [];

                    var monstersThatDropTheItem = _gameState.Monsters.FindAll(monster =>
                        monster.Drops.Find(drop => drop.Code == code) is not null
                    );

                    if (monstersThatDropTheItem is null)
                    {
                        return new AppError(
                            $"The item with code {code} is unobtainable",
                            ErrorStatus.NotFound
                        );
                    }

                    monstersThatDropTheItem.Sort(
                        (a, b) =>
                        {
                            int aDropRate = a.Drops.Find(drop => drop.Code == code)!.Rate;
                            int bDropRate = b.Drops.Find(drop => drop.Code == code)!.Rate;

                            // The lower the number, the higher the drop rate, so we want to sort them like this;
                            return aDropRate.CompareTo(bDropRate);
                        }
                    );

                    foreach (var monster in monstersThatDropTheItem)
                    {
                        if (
                            FightSimulator
                                .CalculateFightOutcome(_playerCharacter.Character, monster)
                                .ShouldFight
                        )
                        {
                            jobs.Add(
                                new FightMonster(
                                    _playerCharacter,
                                    _gameState,
                                    monster.Code,
                                    requiredAmount,
                                    code
                                )
                            );
                            return new None();
                        }
                    }

                    if (monstersThatDropTheItem is null)
                    {
                        return new AppError(
                            $"Cannot fight any monsters that drop item {code} - {_playerCharacter.Character.Name} would lose",
                            ErrorStatus.InsufficientSkill
                        );
                    }
                }
            }
        }

        return new None();
    }
}
