using Application;
using Application.Jobs;

namespace Api.Endpoints;

public static class TrainCombatEndpoint
{
    public static async Task<IResult> ProcessAsync(
        string name,
        TrainRequest request,
        GameState gameState
    )
    {
        var matchingCharacter = gameState.Characters.FirstOrDefault(character =>
            character.Schema.Name == name
        );

        if (matchingCharacter is null)
        {
            return TypedResults.NotFound();
        }

        matchingCharacter.Suspend(false);

        if (request.Level < 0)
        {
            return TypedResults.BadRequest();
        }
        var job = new TrainCombat(matchingCharacter, gameState, request.Level, request.Relative);

        if (request.Idle)
        {
            matchingCharacter.AddIdleJob(job);
        }
        else
        {
            matchingCharacter.QueueJob(job);
        }

        matchingCharacter.Unsuspend();

        return TypedResults.NoContent();
    }
}
