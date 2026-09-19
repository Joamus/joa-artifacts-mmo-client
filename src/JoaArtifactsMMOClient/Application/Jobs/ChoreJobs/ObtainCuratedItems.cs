using Application.ArtifactsApi.Schemas;
using Application.Character;
using Application.Dtos;
using Application.Errors;
using Application.Jobs.Chores;
using Application.Services;
using Applicaton.Jobs;
using Applicaton.Jobs.Chores;
using Microsoft.OpenApi.Extensions;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public class ObtainCuratedItems : CharacterJob, ICharacterChoreJob
{
    List<CuratedItem> CuratedItems { get; set; } =
        [
            new CuratedItem { Code = "healing_rune", Quantity = 5 },
            new CuratedItem { Code = "lifesteal_rune", Quantity = 3 },
            new CuratedItem { Code = "burn_rune", Quantity = 3 },
            new CuratedItem { Code = "protection_rune", Quantity = 1 },
            new CuratedItem { Code = "lost_world_map", Quantity = 5 },
            new CuratedItem { Code = "perfect_pearl", Quantity = 5 },
            new CuratedItem { Code = "cultist_cloak", Quantity = 5 },
            new CuratedItem { Code = "healing_aura_rune", Quantity = 5 },
            new CuratedItem { Code = "vampiric_rune", Quantity = 5 },
            new CuratedItem
            {
                Code = "greater_healing_rune",
                Quantity = 5,
                Superseeds = ["healing_rune", "healing_aura_rune"],
            },
            new CuratedItem
            {
                Code = "greater_lifesteal_rune",
                Quantity = 3,
                Superseeds = ["lifesteal_rune"],
            },
            new CuratedItem
            {
                Code = "greater_protection_rune",
                Quantity = 1,

                Superseeds = ["protection_rune"],
            },
        ];
    GambleTasksCoinsParams JobParams { get; init; }

    public ObtainCuratedItems(
        PlayerCharacter playerCharacter,
        GameState gameState,
        ChorePriority priority
    )
        : base(playerCharacter, gameState)
    {
        JobParams = GetJobParams(priority);
    }

    protected override async Task<OneOf<AppError, None>> ExecuteAsync()
    {
        logger.LogInformation($"{JobName}: [{Character.Schema.Name}] run started");

        var jobs = await GetJobs();

        if (jobs is not null)
        {
            await Character.QueueJobsAfter(Id, jobs);
        }

        return new None();
    }

    public async Task<List<CharacterJob>> GetJobs()
    {
        return [];
    }

    public async Task<bool> NeedsToBeDone()
    {
        var jobs = await GetJobs();

        return jobs.Count > 0;
    }

    static GambleTasksCoinsParams GetJobParams(ChorePriority priority)
    {
        return priority switch
        {
            _ => new GambleTasksCoinsParams
            {
                MinimumCoinsThreshold = 100,
                MinimumQuantityOfTaskItems = 10,
            },
        };
    }

    public bool IsAboveThreshold(int amountOfTasksCoins)
    {
        return amountOfTasksCoins > JobParams.MinimumCoinsThreshold;
    }
}

public record CuratedItem
{
    public required string Code { get; init; }
    public required int Quantity { get; init; }

    // This item is effectively a better replacement for other item(s)
    public List<string>? Superseeds { get; init; }
}
