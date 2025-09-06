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
            .MapPost("/{name}/job/clearAll", ClearAll)
            .WithName(nameof(ClearAll))
            .WithOpenApi()
            .Produces<OneOf<None, AppError>>();

        group
            .MapPost("/{name}/suspend", Suspend)
            .WithName(nameof(Suspend))
            .WithOpenApi()
            .Produces<OneOf<None, AppError>>();

        group.WithOpenApi();

        return app;
    }

    static async Task<IResult> List([FromServices] GameState gameState)
    {
        return TypedResults.Ok(gameState.Characters);
    }

    // static async Task<IResult> Get(Guid? id, AppDbContext dbContext, HttpContext httpContext, AppAuthService authService)
    static async Task<IResult> Get([FromRoute] string name, [FromServices] GameState gameState)
    {
        var matchingCharacter = gameState.Characters.FirstOrDefault(character =>
            character.Character.Name == name
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
        var matchingCharacter = gameState.Characters.FirstOrDefault(character =>
            character.Character.Name == name
        );

        if (matchingCharacter is null)
        {
            return TypedResults.NotFound();
        }

        if (!string.IsNullOrEmpty(request.ItemCode))
        {
            matchingCharacter.QueueJob(
                new FightMonster(matchingCharacter, request.Code, request.Amount, request.ItemCode)
            );
        }
        else
        {
            matchingCharacter.QueueJob(
                new FightMonster(matchingCharacter, request.Code, request.Amount)
            );
        }

        return TypedResults.NoContent();
    }

    static async Task<IResult> ObtainItem(
        [FromRoute] string name,
        [FromBody] ObtainItemRequest request,
        [FromServices] GameState gameState
    )
    {
        var matchingCharacter = gameState.Characters.FirstOrDefault(character =>
            character.Character.Name == name
        );

        if (matchingCharacter is null)
        {
            return TypedResults.NotFound();
        }

        matchingCharacter.QueueJob(
            new ObtainItem(
                matchingCharacter,
                request.Code,
                request.Amount,
                request.UseItemIfInInventory
            )
        );

        return TypedResults.NoContent();
    }

    static async Task<IResult> Gather(
        [FromRoute] string name,
        [FromBody] GatherRequest request,
        [FromServices] GameState gameState
    )
    {
        var matchingCharacter = gameState.Characters.FirstOrDefault(character =>
            character.Character.Name == name
        );

        if (matchingCharacter is null)
        {
            return TypedResults.NotFound();
        }

        matchingCharacter.QueueJob(
            new GatherResource(matchingCharacter, request.Code, request.Amount)
        );

        return TypedResults.NoContent();
    }

    static async Task<IResult> ClearAll([FromRoute] string name, [FromServices] GameState gameState)
    {
        var matchingCharacter = gameState.Characters.FirstOrDefault(character =>
            character.Character.Name == name
        );

        if (matchingCharacter is null)
        {
            return TypedResults.NotFound();
        }

        matchingCharacter.ClearJobs();

        return TypedResults.NoContent();
    }

    static async Task<IResult> Suspend([FromRoute] string name, [FromServices] GameState gameState)
    {
        var matchingCharacter = gameState.Characters.FirstOrDefault(character =>
            character.Character.Name == name
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
}

record FightRequest
{
    public string Code { get; set; }
    public int Amount { get; set; }
    public string ItemCode { get; set; }
}

record ObtainItemRequest
{
    public string Code { get; set; }
    public int Amount { get; set; }
    public bool UseItemIfInInventory { get; set; }
}

record GatherRequest
{
    public string Code { get; set; }
    public int Amount { get; set; }
}
