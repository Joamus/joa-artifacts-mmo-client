using System.Security.Permissions;
using Application;
using Application.Character;
using Application.Errors;
using Application.Jobs;
using Microsoft.AspNetCore.Mvc;
using OneOf;
using OneOf.Types;

namespace Api.Endpoints;

public static class CharacterEndpoints
{
    public static IEndpointRouteBuilder AddCharacterEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/char");

        group
            .MapGet("", List)
            .WithName(nameof(List))
            .WithOpenApi()
            .Produces<IList<PlayerCharacter>>();

        group
            .MapGet("/{name}", Get)
            .WithName(nameof(Get))
            .WithOpenApi()
            .Produces<PlayerCharacter>();

        group
            .MapPost("/{name}/job/fightMonster", FightMonster)
            .WithName(nameof(FightMonster))
            .WithOpenApi()
            .Produces<OneOf<None, AppError>>();

        group
            .MapPost("/{name}/job/obtainItem", ObtainItem)
            .WithName(nameof(ObtainItem))
            .WithOpenApi()
            .Produces<OneOf<None, AppError>>();

        group
            .MapPost("/{name}/job/gather", Gather)
            .WithName(nameof(Gather))
            .WithOpenApi()
            .Produces<OneOf<None, AppError>>();

        group
            .MapPost("/{name}/job/gatherMaterialsForCraftItem", GatherMaterialsForCraftItem)
            .WithName(nameof(GatherMaterialsForCraftItem))
            .WithOpenApi()
            .Produces<OneOf<None, AppError>>();

        group
            .MapPost("/{name}/train/combat", TrainCombat)
            .WithName(nameof(TrainCombat))
            .WithOpenApi()
            .Produces<OneOf<None, AppError>>();

        group
            .MapPost("/{name}/train/skill", TrainSkill)
            .WithName(nameof(TrainSkill))
            .WithOpenApi()
            .Produces<OneOf<None, AppError>>();
        group
            .MapPost("/{name}/task/item", ItemTask)
            .WithName(nameof(ItemTask))
            .WithOpenApi()
            .Produces<OneOf<None, AppError>>();

        group
            .MapPost("/{name}/task/monster", MonsterTask)
            .WithName(nameof(MonsterTask))
            .WithOpenApi()
            .Produces<OneOf<None, AppError>>();

        group
            .MapPost("/{name}/job/clearAll", ClearAll)
            .WithName(nameof(ClearAll))
            .WithOpenApi()
            .Produces<OneOf<None, AppError>>();

        group
            .MapPost("/{name}/interrupt", Interrupt)
            .WithName(nameof(Interrupt))
            .WithOpenApi()
            .Produces<OneOf<None, AppError>>();
        group
            .MapPost("/{name}/suspend", Suspend)
            .WithName(nameof(Suspend))
            .WithOpenApi()
            .Produces<OneOf<None, AppError>>();
        group
            .MapPost("/{name}/unsuspend", Unsuspend)
            .WithName(nameof(Unsuspend))
            .WithOpenApi()
            .Produces<OneOf<None, AppError>>();

        group.WithOpenApi();

        return app;
    }

    static async Task<IResult> List([FromServices] GameState gameState)
    {
        return TypedResults.Ok(gameState.Characters);
    }

    static async Task<IResult> Get([FromRoute] string name, [FromServices] GameState gameState)
    {
        var matchingCharacter = gameState.Characters.FirstOrDefault(character =>
            character.Schema.Name == name
        );

        if (matchingCharacter is null)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(matchingCharacter);
    }

    static async Task<IResult> FightMonster(
        [FromRoute] string name,
        [FromBody] FightRequest request,
        [FromServices] GameState gameState
    )
    {
        return await FightEndpoint.ProcessAsync(name, request, gameState);
    }

    static async Task<IResult> ObtainItem(
        [FromRoute] string name,
        [FromBody] ObtainItemRequest request,
        [FromServices] GameState gameState
    )
    {
        return await ObtainItemEndpoint.ProcessAsync(name, request, gameState);
    }

    static async Task<IResult> Gather(
        [FromRoute] string name,
        [FromBody] GatherRequest request,
        [FromServices] GameState gameState
    )
    {
        return await GatherEndpoint.ProcessAsync(name, request, gameState);
    }

    static async Task<IResult> GatherMaterialsForCraftItem(
        [FromRoute] string name,
        [FromBody] GatherMaterialsForCraftItemEndpointRequest request,
        [FromServices] GameState gameState
    )
    {
        return await GatherMaterialsForCraftItemEndpoint.ProcessAsync(name, request, gameState);
    }

    static async Task<IResult> TrainSkill(
        [FromRoute] string name,
        [FromBody] TrainSkillRequest request,
        [FromServices] GameState gameState
    )
    {
        return await TrainSkillEndpoint.ProcessAsync(name, request, gameState);
    }

    static async Task<IResult> TrainCombat(
        [FromRoute] string name,
        [FromBody] TrainRequest request,
        [FromServices] GameState gameState
    )
    {
        return await TrainCombatEndpoint.ProcessAsync(name, request, gameState);
    }

    static async Task<IResult> ItemTask(
        [FromRoute] string name,
        [FromBody] ItemTaskRequest request,
        [FromServices] GameState gameState
    )
    {
        return await ItemTaskEndpoint.ProcessAsync(name, request, gameState);
    }

    static async Task<IResult> MonsterTask(
        [FromRoute] string name,
        [FromBody] MonsterTaskRequest request,
        [FromServices] GameState gameState
    )
    {
        return await MonsterTaskEndpoint.ProcessAsync(name, request, gameState);
    }

    static async Task<IResult> ClearAll([FromRoute] string name, [FromServices] GameState gameState)
    {
        var matchingCharacter = gameState.Characters.FirstOrDefault(character =>
            character.Schema.Name == name
        );

        if (matchingCharacter is null)
        {
            return TypedResults.NotFound();
        }

        matchingCharacter.ClearJobs();

        return TypedResults.NoContent();
    }

    static async Task<IResult> Interrupt(
        [FromRoute] string name,
        [FromServices] GameState gameState
    )
    {
        var matchingCharacter = gameState.Characters.FirstOrDefault(character =>
            character.Schema.Name == name
        );

        if (matchingCharacter is null)
        {
            return TypedResults.NotFound();
        }

        // matchingCharacter.SetBusy(true);
        if (matchingCharacter.CurrentJob is not null)
        {
            matchingCharacter.CurrentJob.Interrrupt();
        }

        return TypedResults.NoContent();
    }

    static async Task<IResult> Suspend([FromRoute] string name, [FromServices] GameState gameState)
    {
        var matchingCharacter = gameState.Characters.FirstOrDefault(character =>
            character.Schema.Name == name
        );

        if (matchingCharacter is null)
        {
            return TypedResults.NotFound();
        }

        // matchingCharacter.SetBusy(true);
        matchingCharacter.Suspend();

        return TypedResults.NoContent();
    }

    static async Task<IResult> Unsuspend(
        [FromRoute] string name,
        [FromServices] GameState gameState
    )
    {
        var matchingCharacter = gameState.Characters.FirstOrDefault(character =>
            character.Schema.Name == name
        );

        if (matchingCharacter is null)
        {
            return TypedResults.NotFound();
        }

        matchingCharacter.Unsuspend();

        return TypedResults.NoContent();
    }
}

public record GenericActionRequest
{
    public int Repeat { get; set; } = 1;
    public bool Idle { get; set; } = false;
}
