using Application.ArtifactsApi.Schemas;
using Application.Character;
using Application.Dtos;
using Application.Errors;
using Applicaton.Services.FightSimulator;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public class MonsterTask : CharacterJob
{
    public MonsterTask(PlayerCharacter playerCharacter, GameState gameState)
        : base(playerCharacter, gameState) { }

    public override Task<OneOf<AppError, None>> RunAsync()
    {
        _logger.LogInformation(
            $"{GetType().Name} run started - for {_playerCharacter.Character.Name}"
        );

        List<CharacterJob> jobs = [];

        if (_playerCharacter.Character.TaskType == "")
        {
            // Go pick up task - then we should continue
            _playerCharacter.QueueJobsBefore(
                Id,
                [new AcceptNewTask(_playerCharacter, _gameState, TaskType.monsters)]
            );
            return Task.FromResult<OneOf<AppError, None>>(new None());
        }

        if (_playerCharacter.Character.TaskType == TaskType.monsters.ToString())
        {
            var code = _playerCharacter.Character.Task;
            MonsterSchema? monster = _gameState.Monsters.FirstOrDefault(monster =>
                monster.Code == code!
            );
            if (monster is null)
            {
                return Task.FromResult<OneOf<AppError, None>>(
                    new AppError($"Cannot find monster {code} to fight in task")
                );
            }
            var outcome = FightSimulator.CalculateFightOutcome(_playerCharacter.Character, monster);

            if (!outcome.ShouldFight)
            {
                return Task.FromResult<OneOf<AppError, None>>(
                    new AppError(
                        $"Cannot complete monster task, because the monster is too strong - outcome: {outcome.ShouldFight} - remaining monster hp: {outcome.MonsterHp} - monster {code} to fight in task"
                    )
                );
            }
        }
        else
        {
            return Task.FromResult<OneOf<AppError, None>>(
                new AppError(
                    $"Cannot do a {GetType().Name}, because the current task is {_playerCharacter.Character.TaskType}"
                )
            );
        }

        int progressAmount = _playerCharacter.Character.TaskProgress;
        int amount = _playerCharacter.Character.TaskTotal;

        int remainingToKill = amount - progressAmount;
        if (remainingToKill > 0)
        {
            jobs.Add(
                new FightMonster(
                    _playerCharacter,
                    _gameState,
                    _playerCharacter.Character.Task,
                    amount - progressAmount
                )
            );
        }

        jobs.Add(new CompleteTask(_playerCharacter, _gameState));

        _playerCharacter.QueueJobsAfter(Id, jobs);

        _logger.LogInformation(
            $"{GetType().Name} - found {jobs.Count} jobs to run, to complete task {Code} for {_playerCharacter.Character.Name}"
        );

        return Task.FromResult<OneOf<AppError, None>>(new None());
    }
}
