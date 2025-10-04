using Application;
using Application.Jobs;

namespace Api.Endpoints;

public static class ObtainItemEndpoint
{
    public static async Task<IResult> ProcessAsync(
        string name,
        ObtainItemRequest request,
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
            var job = new ObtainItem(matchingCharacter, gameState, request.Code, request.Amount);
            // job.AllowUsingMaterialsFromInventory = request.AllowUsingMaterialsFromInventory;
            job.AllowUsingMaterialsFromBank = request.AllowUsingMaterialsFromBank;

            if (request.ForBank)
            {
                job.ForBank();
            }
            else if (!string.IsNullOrEmpty(request.ForCharacter))
            {
                var recipientCharacter = gameState.Characters.FirstOrDefault(character =>
                    character.Schema.Name == request.ForCharacter
                );

                if (recipientCharacter is null)
                {
                    return TypedResults.NotFound(
                        $"Recipient character \"{request.ForCharacter}\" not found"
                    );
                }

                job.ForCharacter(recipientCharacter);
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

public record ObtainItemRequest : GenericActionRequest
{
    public required string Code { get; set; }
    public int Amount { get; set; } = 1;
    public bool AllowUsingMaterialsFromBank { get; set; } = true;

    // public required bool AllowUsingMaterialsFromInventory { get; set; } = true;
    //
    public bool ForBank { get; set; } = true;
    public string? ForCharacter { get; set; } = null;
}
