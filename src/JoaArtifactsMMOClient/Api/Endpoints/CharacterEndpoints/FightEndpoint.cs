using Api.Endpoints;
using Application;
using Application.Jobs;

namespace Api.Endpoints;

public static class FightEndpoint
{
    public static async Task<IResult> FightMonster(
        string name,
        FightRequest request,
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

        if (!string.IsNullOrEmpty(request.ItemCode))
        {
            var job = new FightMonster(
                matchingCharacter,
                gameState,
                request.Code,
                request.Amount,
                request.ItemCode
            );

            job.AllowUsingMaterialsFromInventory = request.AllowUsingMaterialsFromInventory;

            matchingCharacter.QueueJob(job);
        }
        else
        {
            matchingCharacter.QueueJob(
                new FightMonster(matchingCharacter, gameState, request.Code, request.Amount)
            );
        }

        matchingCharacter.Unsuspend();

        return TypedResults.NoContent();
    }
}

public record FightRequest : GenericActionRequest
{
    public required string Code { get; set; }
    public required int Amount { get; set; }
    public string? ItemCode { get; set; }
    public required bool AllowUsingMaterialsFromInventory { get; set; } = false;
}
