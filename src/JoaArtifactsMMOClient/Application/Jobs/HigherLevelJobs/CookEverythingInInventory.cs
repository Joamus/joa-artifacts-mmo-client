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

    protected override async Task<OneOf<AppError, None>> ExecuteAsync()
    {
        var jobs = GetJobs().Cast<CharacterJob>().ToList();

        if (jobs.Count > 0)
        {
            Character.QueueJobsAfter(Id, jobs);
        }

        return new None();
    }

    public List<ObtainItem> GetJobs()
    {
        List<DropSchema> ingredients = [];

        foreach (var item in Character.Schema.Inventory)
        {
            ingredients.Add(new DropSchema { Code = item.Code, Quantity = item.Quantity });
        }

        var jobs = ItemService.GetFoodObtainJobsFromIngredientList(
            Character,
            gameState,
            ingredients
        );

        return jobs;
    }
}
