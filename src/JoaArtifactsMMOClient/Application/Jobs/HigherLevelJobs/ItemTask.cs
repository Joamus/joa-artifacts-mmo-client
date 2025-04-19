using Application.ArtifactsApi.Schemas;
using Application.Character;
using Application.Errors;
using Applicaton.Services.FightSimulator;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public class ItemTask : CharacterJob
{
    public ItemTask(PlayerCharacter playerCharacter)
        : base(playerCharacter) { }

    public override Task<OneOf<JobError, None>> RunAsync()
    {
        _logger.LogInformation(
            $"{GetType().Name} run started - for {_playerCharacter._character.Name}"
        );

        List<CharacterJob> jobs = [];

        if (_playerCharacter._character.TaskType == "")
        {
            // Go pick up task - then we should continue
            _playerCharacter.QueueJobsBefore(Id, [new TakeTask(_playerCharacter, "items")]);
            return Task.FromResult<OneOf<JobError, None>>(new None());
        }

        if (_playerCharacter._character.TaskType == "items")
        {
            MonsterSchema? monster = _gameState.Monsters.FirstOrDefault(monster =>
                monster.Code == _code!
            );
            if (monster is null)
            {
                return Task.FromResult<OneOf<JobError, None>>(
                    new JobError($"Cannot find monster {_code} to fight in task")
                );
            }
            var outcome = FightSimulatorService.CalculateFightOutcome(
                _playerCharacter._character,
                monster
            );

            if (!outcome.ShouldFight)
            {
                return Task.FromResult<OneOf<JobError, None>>(
                    new JobError(
                        $"Cannot complete monster task, because the monster is too strong - outcome: {outcome.ShouldFight} - remaining monster hp: {outcome.MonsterHp} - monster {_code} to fight in task"
                    )
                );
            }
        }
        else
        {
            return Task.FromResult<OneOf<JobError, None>>(
                new JobError(
                    $"Cannot do a {GetType().Name}, because the current task is {_playerCharacter._character.TaskType}"
                )
            );
        }

        int progressAmount = _playerCharacter._character.TaskProgress;
        int amount = _playerCharacter._character.TaskTotal;

        int remainingToGather = amount - progressAmount;
        if (remainingToGather > 0)
        {
            jobs.Add(
                new ObtainItem(
                    _playerCharacter,
                    _playerCharacter._character.Task,
                    amount - progressAmount,
                    true
                )
            );
        }

        jobs.Add(new CompleteTask(_playerCharacter));

        _playerCharacter.QueueJobsAfter(Id, jobs);

        _logger.LogInformation(
            $"{GetType().Name} - found {jobs.Count} jobs to run, to complete task {_code} for {_playerCharacter._character.Name}"
        );

        return Task.FromResult<OneOf<JobError, None>>(new None());
    }
}
