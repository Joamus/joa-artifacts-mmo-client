using Application.ArtifactsApi.Schemas;
using Application.ArtifactsApi.Schemas.Responses;
using Application.Character;
using Application.Errors;
using Application.Records;
using Application.Services;
using Application.Services.ApiServices;
using Applicaton.Services.FightSimulator;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public class ObtainSuitableFood : CharacterJob
{
    private readonly int _amount;

    public ObtainSuitableFood(PlayerCharacter playerCharacter, GameState gameState, int amount)
        : base(playerCharacter, gameState)
    {
        _amount = amount;
    }

    protected override async Task<OneOf<AppError, None>> ExecuteAsync()
    {
        logger.LogInformation(
            $"{JobName} run started - for {Character.Schema.Name} - need to find {_amount} food"
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
                    $"{JobName} found {jobs.Count} jobs for {Character.Schema.Name} - need to find {_amount} food"
                );
                Character.QueueJobsAfter(Id, jobs);
                break;
        }

        return new None();
    }

    private async Task<OneOf<AppError, List<CharacterJob>>> GetJobsToObtainFood()
    {
        var accountRequester = GameServiceProvider.GetInstance().GetService<AccountRequester>()!;

        var result = await accountRequester.GetBankItems();

        if (result is not BankItemsResponse bankItemsResponse)
        {
            return new AppError("Failed to get bank items");
        }

        int amountFound = 0;

        List<CharacterJob> jobs = [];

        List<ItemInInventory> foodCandidates = [];

        foreach (var item in bankItemsResponse.Data)
        {
            var matchingItem = gameState.Items.FirstOrDefault(_item => _item.Code == item.Code);

            // Should not happen, but handle later maybe
            if (matchingItem is null)
            {
                continue;
            }

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
            int amountToTake = Math.Min(_amount - amountFound, item.Quantity);

            jobs.Add(new WithdrawItem(Character, gameState, item.Item.Code, amountToTake));

            amountFound += Math.Min(_amount - amountFound, item.Quantity);

            if (amountFound >= _amount)
            {
                break;
            }
        }

        if (amountFound >= _amount)
        {
            return jobs;
        }

        var mostSuitableFood = GetMostSuitableFood();

        jobs.Add(
            new ObtainItem(Character, gameState, mostSuitableFood.Code, _amount - amountFound)
        );

        // Just in case we end up with a lot of ingredients in our inventory, we might as well try to cook them.
        // It's not really a perfect system, because it would be better to try to pick ingredients, but eh
        jobs.Add(new CookEverythingInInventory(Character, gameState));

        return jobs;
    }

    private ItemSchema GetMostSuitableFood()
    {
        var viableFood = gameState.Items.FindAll(item =>
        {
            if (item.Subtype != "food")
            {
                return false;
            }

            // We don't really care about the level difference atm, because we just need to obtain the best available food

            if (item.Level > Character.Schema.Level)
            {
                // Find out if you can craft it, if it's craftable. We should probably bias fishing, seeing as it's usually the fastest way to get food
                // and also a good way for our characters to keep their level up in fishing
                return false;
            }

            return true;
        });

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

                var matchInDict = gameState.ItemsDict.ContainsKey(ingredient.Code);
                if (matchInDict)
                {
                    var itemInDict = gameState.ItemsDict[ingredient.Code];

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
