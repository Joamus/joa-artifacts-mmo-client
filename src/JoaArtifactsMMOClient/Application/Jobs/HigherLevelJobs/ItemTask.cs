using Application.ArtifactsApi.Schemas;
using Application.Character;
using Application.Dtos;
using Application.Errors;
using Applicaton.Services.FightSimulator;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public class ItemTask : CharacterJob
{
    public ItemTask(PlayerCharacter playerCharacter, GameState gameState)
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
                [new AcceptNewTask(_playerCharacter, _gameState, TaskType.items)]
            );
            return Task.FromResult<OneOf<AppError, None>>(new None());
        }

        // For gather tasks, we are only going to get tasks that we have high enough skill to do, which means that it shouldn't be needed
        // to check if we have enough skill etc. to complete the task
        if (_playerCharacter.Character.TaskType != TaskType.items.ToString())
        {
            return Task.FromResult<OneOf<AppError, None>>(
                new AppError(
                    $"Cannot do a {GetType().Name}, because the current task is {_playerCharacter.Character.TaskType}"
                )
            );
        }

        int progressAmount = _playerCharacter.Character.TaskProgress;
        int amount = _playerCharacter.Character.TaskTotal;

        int remainingToGather = amount - progressAmount;
        if (remainingToGather > 0)
        {
            jobs.Add(
                new ObtainItem(
                    _playerCharacter,
                    _gameState,
                    _playerCharacter.Character.Task,
                    amount - progressAmount,
                    true
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
