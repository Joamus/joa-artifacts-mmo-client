using System.Collections.Immutable;
using System.Net;
using Application.ArtifactsApi.Schemas;
using Application.Character;
using Application.Errors;
using Application.Services;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public class RestockFood : CharacterJob
{
    const int LOWER_FOOD_THRESHOLD = 25;
    const int HIGHER_FOOD_THRESHOLD = 200;

    public RestockFood(PlayerCharacter playerCharacter, GameState gameState)
        : base(playerCharacter, gameState) { }

    protected override async Task<OneOf<AppError, None>> ExecuteAsync()
    {
        logger.LogInformation($"{JobName}: [{Character.Schema.Name}] run started");

        List<ItemSchema> bestFoodItems = gameState
            .Characters.Select(recipientCharacter =>
                GetIdealFoodForCharacter(Character, recipientCharacter)
            )
            .ToList();

        var bankResponse = await gameState.BankItemCache.GetBankItems(Character);

        Dictionary<string, DropSchema> bestFoodItemsInBank = bankResponse
            .Data.Where(item =>
            {
                if (string.IsNullOrWhiteSpace(item.Code))
                {
                    return false;
                }

                return bestFoodItems.Exists(foodItem => foodItem.Code == item.Code);
            })
            .ToDictionary(item => item.Code);

        List<CharacterJob> jobs = [];

        foreach (var element in bestFoodItemsInBank)
        {
            var food = element.Value;

            var matchingItem = gameState.ItemsDict[food.Code];

            // We don't care that we end up getting more food than strictly needed
            if (food.Quantity <= LOWER_FOOD_THRESHOLD)
            {
                jobs.Add(new ObtainItem(Character, gameState, food.Code, HIGHER_FOOD_THRESHOLD));
            }
        }

        if (jobs.Count > 0)
        {
            await Character.QueueJobsAfter(Id, jobs);
        }

        logger.LogInformation(
            $"{JobName}: [{Character.Schema.Name}] run ended - queued {jobs.Count} jobs to obtain food"
        );
        return new None();
    }

    // We only care about cooking fish
    private ItemSchema GetIdealFoodForCharacter(PlayerCharacter crafter, PlayerCharacter character)
    {
        List<ItemSchema> foodCandidates = gameState
            .Items.Where(item =>
            {
                return IsItemCookedFish(item)
                    && ItemService.CanUseItem(item, character.Schema)
                    && crafter.Schema.CookingLevel >= item.Craft?.Level;
            })
            .ToList();

        // Assume higher lvl food gives more HP
        foodCandidates.Sort((a, b) => b.Level - a.Level);

        return foodCandidates.ElementAt(0);
    }

    private bool IsItemCookedFish(ItemSchema item)
    {
        return item.Subtype == "food"
            && item.Craft is not null
            && item.Craft.Items.Exists(item => gameState.ItemsDict[item.Code].Subtype == "fishing");
    }
}
