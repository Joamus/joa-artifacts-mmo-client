using Application.ArtifactsApi.Schemas;
using Application.Character;
using Application.Errors;
using Application.Records;
using Application.Services;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public class CookEverythingInInventory : CharacterJob
{
    public CookEverythingInInventory(PlayerCharacter playerCharacter, GameState gameState)
        : base(playerCharacter, gameState) { }

    protected override Task<OneOf<AppError, None>> ExecuteAsync()
    {
        var jobs = ItemService
            .GetFoodObtainJobsFromIngredientList(Character, gameState, Character.Schema.Inventory)
            .Cast<CharacterJob>()
            .ToList();

        if (jobs.Count > 0)
        {
            Character.QueueJobsAfter(Id, jobs);
        }

        return Task.FromResult<OneOf<AppError, None>>(new None());
    }
}
