using Application;
using Application.Jobs;

namespace Api.Endpoints;

public static class GatherEndpoint
{
    public static async Task<IResult> ProcessAsync(
        string name,
        GatherRequest request,
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
            var job = new GatherResourceItem(
                matchingCharacter,
                gameState,
                request.Code,
                request.Amount
            );

            if (request.ForBank)
            {
                job.ForBank();
            }

            matchingCharacter.QueueJob(job);
        }

        matchingCharacter.Unsuspend();

        return TypedResults.NoContent();
    }
}

public record GatherRequest : GenericActionRequest
{
    public required string Code { get; set; }
    public required int Amount { get; set; } = 1;
    public int Repeat { get; set; } = 1;

    public bool ForBank { get; set; } = true;
}
