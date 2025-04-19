using Application.ArtifactsApi.Schemas;
using Application.Character;
using Application.Errors;
using Applicaton.Services.FightSimulator;
using Microsoft.VisualBasic;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public class TakeTask : CharacterJob
{
    public TakeTask(PlayerCharacter playerCharacter, string code)
        : base(playerCharacter)
    {
        _code = code;
    }

    public override Task<OneOf<JobError, None>> RunAsync()
    {
        if (_code is null)
        {
            throw new Exception("Code cannot be null here");
        }

        _logger.LogInformation(
            $"{GetType().Name} run started - for {_playerCharacter._character.Name}"
        );

        List<CharacterJob> jobs = [];

        if (_playerCharacter._character.Task != "")
        {
            return Task.FromResult<OneOf<JobError, None>>(
                new JobError($"Character already has a task ${_playerCharacter._character.Task}")
            );
        }

        if (_playerCharacter._character.TaskType == "monsters")
        {
            MonsterSchema monster = _gameState.Monsters.FirstOrDefault(monster =>
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
            jobs.Add(new TakeTask(_playerCharacter, "monsters"));
            // Go pick up quest
        }

        int progressAmount = _playerCharacter._character.TaskProgress;
        int amount = _playerCharacter._character.TaskTotal;

        int remainingToKill = amount - progressAmount;
        if (remainingToKill > 0)
        {
            jobs.Add(
                new FightMonster(
                    _playerCharacter,
                    _playerCharacter._character.Task,
                    amount - progressAmount
                )
            );
        }

        jobs.Add(new CompleteTask(_playerCharacter));

        foreach (var job in jobs)
        {
            _playerCharacter.QueueJob(job);
        }
        _logger.LogInformation(
            $"{GetType().Name} - found {jobs.Count} jobs to run, to complete task {_code} for {_playerCharacter._character.Name}"
        );

        return Task.FromResult<OneOf<JobError, None>>(new None());
    }
}
