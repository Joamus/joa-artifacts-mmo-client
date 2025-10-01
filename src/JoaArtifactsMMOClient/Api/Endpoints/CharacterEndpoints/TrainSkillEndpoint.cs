using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using Api.Endpoints;
using Application;
using Application.Artifacts.Schemas;
using Application.Jobs;

namespace Api.Endpoints;

public static class TrainSkillEndpoint
{
    public static async Task<IResult> ProcessAsync(
        string name,
        TrainSkillRequest request,
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

        var until =
            request.RelativeLevel > 0
                ? matchingCharacter.Schema.Level + request.RelativeLevel
                : request.UntilLevel;

        if (until < 0)
        {
            return TypedResults.BadRequest();
        }

        matchingCharacter.QueueJob(
            new TrainSkill(matchingCharacter, gameState, request.Skill, until)
        );

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

public record TrainSkillRequest : TrainRequest
{
    [JsonPropertyName("Skill")]
    public required Skill Skill { get; set; }
}

public record TrainRequest : GenericActionRequest
{
    public int UntilLevel { get; set; } = 0;
    public int RelativeLevel { get; set; } = 0;
}
