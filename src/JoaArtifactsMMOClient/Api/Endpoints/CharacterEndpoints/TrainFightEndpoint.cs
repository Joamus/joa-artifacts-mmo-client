using Api.Endpoints;
using Application;
using Application.Jobs;

namespace Api.Endpoints;

public static class TrainFightEndpoint
{
    public static async Task<IResult> TrainFight(
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

        matchingCharacter.Suspend();

        // if (!string.IsNullOrEmpty(request.ItemCode))
        // {
        //     matchingCharacter.QueueJob(
        //         new FightMonster(
        //             matchingCharacter,
        //             gameState,
        //             request.Code,
        //             request.Amount,
        //             request.ItemCode,
        //             request.UseItemIfInInventory
        //         )
        //     );
        // }
        // else
        // {
        //     matchingCharacter.QueueJob(
        //         new FightMonster(matchingCharacter, gameState, request.Code, request.Amount)
        //     );
        // }

        matchingCharacter.Unsuspend();

        return TypedResults.NoContent();
    }
}
