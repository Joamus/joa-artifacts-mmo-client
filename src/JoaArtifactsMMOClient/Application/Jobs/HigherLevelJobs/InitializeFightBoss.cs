using Application.ArtifactsApi.Schemas;
using Application.Character;
using Application.Errors;
using Application.Jobs.Orchestrators;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public class InitializeFightBoss : CharacterJob
{
    InitializeFightBossJobParams JobParams { get; init; }

    public InitializeFightBoss(InitializeFightBossJobParams jobParams)
        : base(jobParams.Character, jobParams.GameState)
    {
        JobParams = jobParams;
    }

    protected override async Task<OneOf<AppError, None>> ExecuteAsync()
    {
        var result = await FightBossOrchestrator.InitializeFightBossJob(JobParams);

        return result.Match<OneOf<AppError, None>>(error => error, _ => new None());
    }
}

public record InitializeFightBossJobParams
{
    public required PlayerCharacter Character { get; set; }
    public required GameState GameState { get; set; }
    public required List<PlayerCharacter> OtherCharacters { get; set; }
    public required MonsterSchema Monster { get; set; }
    public required string? ItemCode { get; set; }
    public required int Amount { get; set; }
    public required bool AllowUsingMaterialsFromInventory;
}
