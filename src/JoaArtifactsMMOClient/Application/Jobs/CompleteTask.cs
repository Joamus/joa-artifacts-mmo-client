using Application.Character;
using Application.Errors;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public class CompleteTask : CharacterJob
{
    public CompleteTask(PlayerCharacter playerCharacter, GameState gameState)
        : base(playerCharacter, gameState)
    {
        Code = _playerCharacter.Character.TaskType;
    }

    public override async Task<OneOf<AppError, None>> RunAsync()
    {
        if (
            _playerCharacter.Character.Task == ""
            || _playerCharacter.Character.TaskProgress < _playerCharacter.Character.TaskTotal
        )
        {
            // Cannot complete quest, ignore for now
            return new None();
        }

        _logger.LogInformation(
            $"{GetType().Name} run started - for {_playerCharacter.Character.Name} - task ${Code}"
        );

        await _playerCharacter.NavigateTo(Code!, ArtifactsApi.Schemas.ContentType.TasksMaster);

        await _playerCharacter.TaskComplete();

        var taskCoins = _playerCharacter.Character.Inventory.FirstOrDefault(item =>
            item.Code == "tasks_coin"
        );

        if (taskCoins is not null && taskCoins.Quantity >= 6)
        {
            await _playerCharacter.TaskExchange();
        }

        _logger.LogInformation(
            $"{GetType().Name} run complete - for {_playerCharacter.Character.Name} - task ${Code}"
        );

        return new None();
    }
}
