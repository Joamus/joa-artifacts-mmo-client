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

    protected override async Task<OneOf<AppError, None>> ExecuteAsync()
    {
        if (Code is null)
        {
            throw new Exception("Code cannot be null here");
        }

        logger.LogInformation($"{GetType().Name}: [{Character.Schema.Name}] run started");

        List<CharacterJob> jobs = [];

        if (Character.Schema.Task != "")
        {
            return new AppError($"Character already has a task {Character.Schema.Task}");
        }

        await Character.NavigateTo("monsters", ContentType.TasksMaster);
        await Character.TaskNew();

        logger.LogInformation(
            $"{GetType().Name}: [{Character.Schema.Name}] - found {jobs.Count} jobs to run, to complete task {Code} for {Character.Schema.Name}"
        );

        return new None();
    }
}
