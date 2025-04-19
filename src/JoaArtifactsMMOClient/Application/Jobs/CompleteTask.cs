using Application.Character;
using Application.Errors;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public class CompleteTask : CharacterJob
{
    public CompleteTask(PlayerCharacter playerCharacter)
        : base(playerCharacter)
    {
        _code = _playerCharacter._character.TaskType;
    }

    public override async Task<OneOf<JobError, None>> RunAsync()
    {
        if (
            _playerCharacter._character.Task == ""
            || _playerCharacter._character.TaskProgress < _playerCharacter._character.TaskTotal
        )
        {
            // Cannot complete quest, ignore for now
            return new None();
        }

        _logger.LogInformation(
            $"{GetType().Name} run started - for {_playerCharacter._character.Name} - task ${_code}"
        );

        await _playerCharacter.NavigateTo(_code!, ArtifactsApi.Schemas.ContentType.TasksMaster);

        await _playerCharacter.TaskComplete();

        _logger.LogInformation(
            $"{GetType().Name} run complete - for {_playerCharacter._character.Name} - task ${_code}"
        );

        return new None();
    }
}
