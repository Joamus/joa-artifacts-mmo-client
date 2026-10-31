using Application.Character;
using Application.Errors;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public class SeasonalOfferingJob : CharacterJob
{
    public SeasonalOfferingJob(PlayerCharacter playerCharacter, GameState gameState)
        : base(playerCharacter, gameState) { }

    protected override async Task<OneOf<AppError, None>> ExecuteAsync()
    {
        var jobs = await gameState.Services.SeasonalOfferingService.GetOfferingJobs(Character);

        if (jobs.Count == 0)
        {
            return new None();
        }

        logger.LogInformation(
            $"{JobName}: [{Character.Schema.Name}] found {jobs.Count} jobs to queue, to accept/continue with seasonal offerings"
        );

        jobs = [.. jobs.Select(job => job.SetParent<CharacterJob>(this))];

        await Character.QueueJobsAfter(Id, jobs);

        return new None();
    }

    public static async Task<bool> CanDoJob(PlayerCharacter character, GameState gameState)
    {
        var jobs = await gameState.Services.SeasonalOfferingService.GetOfferingJobs(character);

        return jobs.Count > 0;
    }
}
