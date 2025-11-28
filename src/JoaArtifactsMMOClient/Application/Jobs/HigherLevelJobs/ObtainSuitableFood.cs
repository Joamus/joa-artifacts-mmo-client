using Application.ArtifactsApi.Schemas;
using Application.ArtifactsApi.Schemas.Responses;
using Application.Character;
using Application.Errors;
using Application.Records;
using Application.Services;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public class ObtainSuitableFood : CharacterJob
{
    public ObtainSuitableFood(PlayerCharacter playerCharacter, GameState gameState, int amount)
        : base(playerCharacter, gameState)
    {
        Amount = amount;
    }

    protected override async Task<OneOf<AppError, None>> ExecuteAsync()
    {
        logger.LogInformation(
            $"{JobName} run started - for {Character.Schema.Name} - need to find {Amount} food"
        );
        // Look in bank if we have any that is usable, just take the lowest level food, so we can clean out
        // If we have don't have enough, take uncooked food (if you can cook it), and cook it

        // If still not enough, find

        // If still not enough, we just go gather and cook some - be biased towards fishing, fastest way to get food

        var result = await GetJobsToObtainFood();

        switch (result.Value)
        {
            case AppError jobError:
                return jobError;
            case List<CharacterJob> jobs:
                logger.LogInformation(
                    $"{JobName} found {jobs.Count} jobs for {Character.Schema.Name} - need to find {Amount} food"
                );
                Character.QueueJobsAfter(Id, jobs);
                break;
        }

        return new None();
    }

    private async Task<OneOf<AppError, List<CharacterJob>>> GetJobsToObtainFood()
    {
        var result = await gameState.BankItemCache.GetBankItems(Character);

        if (result is not BankItemsResponse bankItemsResponse)
        {
            return new AppError("Failed to get bank items");
        }

        int amountFound = 0;

        List<CharacterJob> jobs = [];

        // Prioritize cooking stuff from inventory before running to bank - should be faster,
        // and we won't end up with a lot of uncooked stuff.

        var jobsToCookFoodInInventory = new CookEverythingInInventory(
            Character,
            gameState
        ).GetJobs();

        foreach (var job in jobsToCookFoodInInventory)
        {
            amountFound += job.Amount;
            jobs.Add(job);
        }

        if (amountFound >= Amount)
        {
            return jobs;
        }

        List<ItemInInventory> foodCandidates = [];

        foreach (var item in bankItemsResponse.Data)
        {
            var matchingItem = gameState.ItemsDict[item.Code];

            // int levelDifference = _playerCharacter._character.Level - matchingItem.Level;
            // If item is null, then it has been deleted from the game or something
            if (
                matchingItem.Subtype == "food"
                && ItemService.CanUseItem(matchingItem, Character.Schema)
            )
            {
                foodCandidates.Add(
                    new ItemInInventory { Item = matchingItem, Quantity = item.Quantity }
                );
            }
        }

        CalculationService.SortItemsBasedOnEffect(foodCandidates, "heal", true);

        foreach (var item in foodCandidates)
        {
            int amountToTake = Math.Min(Amount - amountFound, item.Quantity);

            jobs.Add(new WithdrawItem(Character, gameState, item.Item.Code, amountToTake));

            amountFound += Math.Min(Amount - amountFound, item.Quantity);

            if (amountFound >= Amount)
            {
                break;
            }
        }

        if (amountFound < Amount)
        {
            // Check if there are uncooked fish, also low level fish - we can end up having a lot of them,
            // and we might as well it eat.
            foreach (var item in bankItemsResponse.Data)
            {
                if (amountFound >= Amount)
                {
                    break;
                }
                var matchingItem = gameState.ItemsDict[item.Code];

                if (matchingItem.Subtype == "fishing")
                {
                    List<ItemSchema>? cookedInto = gameState.CraftingLookupDict.GetValueOrNull(
                        matchingItem.Code
                    );

                    if (cookedInto is not null)
                    {
                        var probablyCookedFishItem = cookedInto.FirstOrDefault(item =>
                            item.Craft is not null
                            && item.Craft?.Items.Count == 0
                            && ItemService.CanUseItem(matchingItem, Character.Schema)
                        );

                        if (probablyCookedFishItem is not null)
                        {
                            int amountToCook = (int)
                                Math.Floor(
                                    (decimal)(
                                        item.Quantity
                                        / probablyCookedFishItem.Craft!.Items[0].Quantity
                                    )
                                );

                            amountToCook = Math.Min(amountToCook, Amount);

                            if (amountToCook > 0)
                            {
                                jobs.Add(
                                    new ObtainItem(
                                        Character,
                                        gameState,
                                        probablyCookedFishItem.Code,
                                        amountToCook
                                    )
                                );
                            }
                        }
                    }
                }
            }
        }

        if (amountFound < Amount)
        {
            var jobsToCookFromBank = ItemService
                .GetFoodToCookFromInventoryList(Character, gameState, result.Data)
                .Select(item =>
                {
                    var job = new ObtainOrFindItem(Character, gameState, item.Code, item.Quantity);
                    job.AllowUsingMaterialsFromBank = true;

                    return job;
                })
                .ToList();

            foreach (var job in jobsToCookFromBank)
            {
                int amountStillNeeded = Math.Min(Amount - amountFound, job.Amount);
                job.Amount = amountStillNeeded;

                amountFound += amountStillNeeded;

                jobs.Add(job);

                job.AllowUsingMaterialsFromBank = true;

                if (amountFound >= Amount)
                {
                    break;
                }
            }

            if (amountFound >= Amount)
            {
                return jobs;
            }

            // if (jobs.Count > 0)
            // {
            //     Character.QueueJobsAfter(Id, jobs);
            // }
            var mostSuitableFood = GetMostSuitableFood();

            var obtainJob = new ObtainOrFindItem(
                Character,
                gameState,
                mostSuitableFood.Code,
                Amount - amountFound
            );

            jobs.Add(obtainJob);
        }

        return jobs;
    }

    private ItemSchema GetMostSuitableFood()
    {
        var viableFood = gameState.Items.FindAll(item =>
            item.Subtype == "food" && ItemService.CanUseItem(item, Character.Schema)
        );

        CalculationService.SortItemsBasedOnEffect(viableFood, "heal");

        ItemSchema? gatherableFood = null;
        // Not supporting this yet - if we are running this job, it's probably because we need to fight, so we want to obtain food asap.
        // Fighting for food is probably not really worth it, and we want our characters to be good at cooking at fishing.
        // if we got this far, it also means that we probably have no food in our inventory, or bank, so it's a bit of a last resort.
        ItemSchema? fightableFood = null;

        foreach (var food in viableFood)
        {
            if (food.Craft?.Items.Count <= 1)
            {
                // Prefer fish, where we just need to cook one fish
                var ingredient = food.Craft.Items[0];

                var itemInDict = gameState.ItemsDict.GetValueOrNull(ingredient.Code);
                if (itemInDict is not null)
                {
                    if (
                        itemInDict.Type == "resource"
                        && itemInDict.Subtype == "fishing"
                        && itemInDict.Level <= Character.Schema.FishingLevel
                    )
                    {
                        gatherableFood = food;
                        break; // don't care about fightable food, if we can gather it
                    }
                }
            }
        }

        return gatherableFood ?? fightableFood ?? gameState.ItemsDict["cooked_gudgeon"]!; // You can cook this from level 1, but this should probably never occur
    }
}
