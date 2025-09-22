using Application;
using Application.Jobs;

namespace Api.Endpoints;

public static class GatherMaterialsForCraftItemEndpoint
{
    public static async Task<IResult> ProcessAsync(
        string name,
        GatherMaterialsForCraftItemEndpointRequest request,
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
            var job = new GatherMaterialsForItem(
                matchingCharacter,
                gameState,
                request.Code,
                request.Amount
            );
            job.AllowUsingMaterialsFromBank = request.AllowUsingMaterialsFromBank;

            if (request.ForBank)
            {
                job.ForBank();
            }
            else if (!string.IsNullOrEmpty(request.CraftBy))
            {
                var recipientCharacter = gameState.Characters.FirstOrDefault(character =>
                    character.Schema.Name == request.CraftBy
                );

                if (recipientCharacter is null)
                {
                    return TypedResults.NotFound(
                        $"Recipient character \"{request.CraftBy}\" not found"
                    );
                }

                job.Character = recipientCharacter;
            }

            if (request.Idle)
            {
                matchingCharacter.SetIdleJob(job);
                break;
            }

            matchingCharacter.QueueJob(job);
        }
        matchingCharacter.Unsuspend();

        return TypedResults.NoContent();
    }
}

public record GatherMaterialsForCraftItemEndpointRequest : GenericActionRequest
{
    public required string Code { get; set; }
    public int Amount { get; set; } = 1;
    public int Repeat { get; set; } = 1;
    public required bool AllowUsingMaterialsFromBank { get; set; } = true;

    public bool ForBank { get; set; } = true;
    public string? CraftBy { get; set; } = null;
}
