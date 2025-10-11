using Api.Endpoints;
using Application;
using Application.Jobs;

namespace Api.Endpoints;

public static class FightEndpoint
{
    public static async Task<IResult> ProcessAsync(
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

        matchingCharacter.Suspend(false);

        for (int i = 0; i < request.Repeat; i++)
        {
            FightMonster? job = null;

            if (!string.IsNullOrEmpty(request.ItemCode))
            {
                job = new FightMonster(
                    matchingCharacter,
                    gameState,
                    request.Code,
                    request.Amount,
                    request.ItemCode
                );

                job.AllowUsingMaterialsFromInventory = request.AllowUsingMaterialsFromInventory;
            }
            else
            {
                job = new FightMonster(matchingCharacter, gameState, request.Code, request.Amount);
            }

            if (request.Idle)
            {
                matchingCharacter.AddIdleJob(job);
                break;
            }
            matchingCharacter.QueueJob(job);
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
    public bool AllowUsingMaterialsFromInventory { get; set; } = false;
}
