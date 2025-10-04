using Application;
using Application.Jobs;

namespace Api.Endpoints;

public static class MonsterTaskEndpoint
{
    public static async Task<IResult> ProcessAsync(
        string name,
        MonsterTaskRequest request,
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
            var job = new MonsterTask(
                matchingCharacter,
                gameState,
                request.ItemCode,
                request.ItemAmount
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

public record MonsterTaskRequest : GenericActionRequest
{
    public bool ForBank { get; set; } = true;
    public string? ItemCode { get; set; }
    public int? ItemAmount { get; set; }
}
