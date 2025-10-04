using Api.Endpoints;
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

        matchingCharacter.QueueJob(
            new TrainCombat(matchingCharacter, gameState, request.Level, request.IsRelative)
        );

        matchingCharacter.Unsuspend();

        return TypedResults.NoContent();
    }
}
