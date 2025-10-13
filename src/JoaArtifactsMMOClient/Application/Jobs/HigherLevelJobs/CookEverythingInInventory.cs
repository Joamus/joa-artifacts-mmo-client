using Application.ArtifactsApi.Schemas;
using Application.Character;
using Application.Errors;
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

    public List<CraftItem> GetJobs()
    {
        List<DropSchema> ingredients = [];

        foreach (var item in Character.Schema.Inventory)
        {
            ingredients.Add(new DropSchema { Code = item.Code, Quantity = item.Quantity });
        }

        var jobs = ItemService
            .GetFoodToCookFromInventoryList(Character, gameState, ingredients)
            .Select(item => new CraftItem(Character, gameState, item.Code, item.Quantity))
            .ToList();

        return jobs;
    }
}
