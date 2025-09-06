using Application.ArtifactsApi.Schemas;
using Application.Character;
using Application.Dtos;
using Application.Errors;
using Applicaton.Services.FightSimulator;
using Microsoft.VisualBasic;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public class AcceptNewTask : CharacterJob
{
    public AcceptNewTask(PlayerCharacter playerCharacter, GameState gameState, TaskType type)
        : base(playerCharacter, gameState)
    {
        Code = type.ToString();
    }

    public override async Task<OneOf<AppError, None>> RunAsync()
    {
        if (Code is null)
        {
            throw new Exception("Code cannot be null here");
        }

        _logger.LogInformation(
            $"{GetType().Name} run started - for {_playerCharacter.Character.Name}"
        );

        List<CharacterJob> jobs = [];

        if (_playerCharacter.Character.Task != "")
        {
            return (
                new AppError($"Character already has a task ${_playerCharacter.Character.Task}")
            );
        }

        await _playerCharacter.NavigateTo("monsters", ContentType.TasksMaster);
        await _playerCharacter.TaskNew();

        _logger.LogInformation(
            $"{GetType().Name} - found {jobs.Count} jobs to run, to complete task {Code} for {_playerCharacter.Character.Name}"
        );

        return new None();
    }
}
