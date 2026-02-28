using System.Collections.Immutable;
using Application.ArtifactsApi.Schemas;
using Application.Character;
using Application.Errors;
using Application.Services;
using Applicaton.Jobs.Chores;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public class RestockFood : CharacterJob, ICharacterChoreJob
{
    const int LOWER_FOOD_THRESHOLD = 150;
    const int HIGHER_FOOD_THRESHOLD = 500;

    public RestockFood(PlayerCharacter playerCharacter, GameState gameState)
        : base(playerCharacter, gameState) { }

    protected override async Task<OneOf<AppError, None>> ExecuteAsync()
    {
        logger.LogInformation($"{JobName}: [{Character.Schema.Name}] run started");

        var jobs = await GetJobs();

        if (jobs.Count > 0)
        {
            await Character.QueueJobsAfter(Id, jobs);
        }

        logger.LogInformation(
            $"{JobName}: [{Character.Schema.Name}] run ended - queued {jobs.Count} jobs to obtain food"
        );
        return new None();
    }

    async Task<List<CharacterJob>> GetJobs()
    {
        List<ItemSchema> bestFoodItems = gameState
            .Characters.Select(recipientCharacter =>
                GetIdealFoodForCharacter(Character, recipientCharacter, gameState)
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

        foreach (var item in bestFoodItems)
        {
            List<int> iterations = ObtainItem.CalculateObtainItemIterations(
                item,
                Character.GetInventorySpaceLeft(),
                HIGHER_FOOD_THRESHOLD
            );

            var matchInBank = bestFoodItemsInBank.GetValueOrDefault(item.Code);

            if (matchInBank is not null && matchInBank.Quantity >= LOWER_FOOD_THRESHOLD)
            {
                continue;
            }

            foreach (var iteration in iterations)
            {
                // We don't care that we end up getting more food than strictly needed
                var job = new ObtainItem(Character, gameState, item.Code, iteration);

                job.ForBank();

                jobs.Add(job);
            }
        }

        return jobs;
    }

    // We only care about cooking fish
    private static ItemSchema GetIdealFoodForCharacter(
        PlayerCharacter crafter,
        PlayerCharacter character,
        GameState gameState
    )
    {
        List<ItemSchema> foodCandidates = gameState
            .Items.Where(item =>
            {
                return ItemService.IsItemCookedFish(item, gameState)
                    && ItemService.CanUseItem(item, character.Schema)
                    && crafter.Schema.CookingLevel >= item.Craft?.Level
                    && item.Craft.Items.Count == 1;
            })
            .ToList();

        // Assume higher lvl food gives more HP
        foodCandidates.Sort((a, b) => b.Level - a.Level);

        return foodCandidates.ElementAt(0);
    }

    public async Task<bool> NeedsToBeDone()
    {
        var jobs = await GetJobs();

        return jobs.Count > 0;
    }
}
