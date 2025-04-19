using Application;
using Application.ArtifactsApi.Schemas;
using Application.Character;
using Application.Errors;
using Application.Jobs;
using Application.Services;
using Applicaton.Jobs;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public class ObtainItem : CharacterJob
{
    private bool _useItemIfInInventory { get; set; } = false;

    protected string _code { get; init; }
    protected int _amount { get; init; }

    protected int _progressAmount { get; set; } = 0;

    public ObtainItem(PlayerCharacter playerCharacter, string code, int amount)
        : base(playerCharacter)
    {
        _code = code;
        _amount = amount;
    }

    public ObtainItem(
        PlayerCharacter playerCharacter,
        string code,
        int amount,
        bool useItemIfInInventory
    )
        : base(playerCharacter)
    {
        _code = code;
        _amount = amount;
        _useItemIfInInventory = useItemIfInInventory;
    }

    public override async Task<OneOf<JobError, None>> RunAsync()
    {
        // It's not very elegant that this job is pasted in multiple places, but a lot of jobs want to have their inventory be clean before they start, or in their InnerJob.
        if (DepositUnneededItems.ShouldInitDepositItems(_playerCharacter))
        {
            _playerCharacter.QueueJobsBefore(Id, [new DepositUnneededItems(_playerCharacter)]);
            return new None();
        }

        List<CharacterJob> jobs = [];
        _logger.LogInformation(
            $"{GetType().Name} run started - for {_playerCharacter._character.Name} - progress {_code} ({_progressAmount}/{_amount})"
        );

        // useItemIfInInventory is set to the job's value at first, so we can allow obtaining an item we already have.
        // But if we have the ingredients in our inventory, then we should always use them (for now).
        // Having this variable will allow us to e.g craft multiple copper daggers, else we could only have 1 in our inventory

        var result = GetJobsRequired(jobs, _code, _amount, _useItemIfInInventory);

        _logger.LogInformation(
            $"{GetType().Name} - found {jobs.Count} jobs to run, to obtain item {_code} for {_playerCharacter._character.Name}"
        );

        switch (result.Value)
        {
            case JobError jobError:
                return jobError;
        }

        _playerCharacter.QueueJobsAfter(Id, jobs);

        return new None();
    }

    /**
     * Get all the jobs required to obtain an item
     * We mutate a list to recursively add all the required jobs to the list
    */
    public OneOf<JobError, None> GetJobsRequired(
        List<CharacterJob> jobs,
        string code,
        int amount,
        bool useItemIfInInventory
    )
    {
        var matchingItem = _gameState.Items.Find(item => item.Code == code);

        if (matchingItem is null)
        {
            return new JobError($"Could not find item with code {code} - could not gather it");
        }

        // We have the item already, no need to get it again

        int amountInInventory = useItemIfInInventory
            ? (_playerCharacter.GetItemFromInventory(code)?.Quantity ?? 0)
            : 0;

        if (useItemIfInInventory && amountInInventory >= amount)
        {
            return new None();
        }

        int requiredAmount = amount - amountInInventory;

        if (matchingItem.Craft is not null)
        {
            foreach (var item in matchingItem.Craft.Items)
            {
                GetJobsRequired(jobs, item.Code, item.Quantity * requiredAmount, true);
            }

            jobs.Add(new CraftItem(_playerCharacter, code, requiredAmount));
        }
        else
        {
            var resources = _gameState.Resources.FindAll(resource =>
                resource.Drops.Find(drop => drop.Code == code && drop.Rate > 0) != null
            );

            if (resources is not null)
            {
                jobs.Add(new GatherResource(_playerCharacter, code, requiredAmount));
            }
            else
            {
                var monstersThatDropTheItem = _gameState.Monsters.Find(monster =>
                    monster.Drops.Find(drop => drop.Code == code) is not null
                );

                if (monstersThatDropTheItem is null)
                {
                    return new JobError(
                        $"The item with code {code} is unobtainable",
                        JobStatus.NotFound
                    );
                }

                jobs.Add(new FightMonster(_playerCharacter, code, requiredAmount));
            }
        }

        return new None();
    }
}
