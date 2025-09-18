using Application.Artifacts.Schemas;
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

public class ObtainSuitablePotions : CharacterJob
{
    private readonly int _amount;

    public ObtainSuitablePotions(PlayerCharacter playerCharacter, GameState gameState, int amount)
        : base(playerCharacter, gameState)
    {
        _amount = amount;
    }

    protected override async Task<OneOf<AppError, None>> ExecuteAsync()
    {
        logger.LogInformation(
            $"{GetType().Name}: [{Character.Schema.Name}] run started - need to find {_amount} potions"
        );
        // Look in bank if we have any that is usable, just take the lowest level food, so we can clean out
        // If we have don't have enough, take uncooked food (if you can cook it), and cook it

        // If still not enough, find


        // If still not enough, we just go gather and cook some - be biased towards fishing, fastest way to get food

        var result = await GetJobsToObtainPotions();

        switch (result.Value)
        {
            case AppError jobError:
                return jobError;
            case List<CharacterJob> jobs:
                logger.LogInformation(
                    $"{GetType().Name}: [{Character.Schema.Name}] found {jobs.Count} jobs - need to find {_amount} potions"
                );
                if (jobs.Count > 0)
                {
                    Character.QueueJobsAfter(Id, jobs);
                }
                break;
        }

        return new None();
    }

    private async Task<OneOf<AppError, List<CharacterJob>>> GetJobsToObtainPotions()
    {
        var result = await gameState.AccountRequester.GetBankItems();

        if (result is not BankItemsResponse bankItemsResponse)
        {
            return new AppError("Failed to get bank items");
        }

        int amountFound = 0;

        List<CharacterJob> jobs = [];

        List<ItemInInventory> potionCandiates = [];

        foreach (var item in bankItemsResponse.Data)
        {
            var matchingItem = gameState.UtilityItemsDict.GetValueOrNull(item.Code);

            // Should not happen, but handle later maybe
            if (matchingItem is null || matchingItem.Subtype != "potion")
            {
                continue;
            }

            // int levelDifference = _playerCharacter._character.Level - matchingItem.Level;
            // If item is null, then it has been deleted from the game or something
            if (
                matchingItem.Effects.Find(effect => effect.Code == "restore") is not null
                && ItemService.CanUseItem(matchingItem, Character.Schema.Level)
            )
            {
                potionCandiates.Add(
                    new ItemInInventory { Item = matchingItem, Quantity = item.Quantity }
                );
            }
        }

        // We want to always use the best pots we can
        CalculationService.SortItemsBasedOnEffect(potionCandiates, "restore", false);

        foreach (var item in potionCandiates)
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

        var mostSuitablePotion = GetMostSuitablePotion(Character, gameState);

        if (mostSuitablePotion is not null)
        {
            jobs.Add(new ObtainItem(Character, gameState, mostSuitablePotion.Code, _amount));
        }

        return jobs;
    }

    public static ItemSchema? GetMostSuitablePotion(PlayerCharacter character, GameState gameState)
    {
        ItemSchema? bestPotionThatCanBeCrafted = null;

        foreach (var element in gameState.UtilityItemsDict)
        {
            var item = element.Value;
            int restoreEffect = ItemService.GetEffect(item, "restore");

            if (restoreEffect == 0)
            {
                continue;
            }

            var canCraftItem =
                item.Craft?.Skill == Skill.Alchemy
                && character.Schema.AlchemyLevel >= item.Craft.Level;

            if (!canCraftItem || !ItemService.CanUseItem(item, character.Schema.Level))
            {
                continue;
            }

            if (
                bestPotionThatCanBeCrafted is null
                || ItemService.GetEffect(bestPotionThatCanBeCrafted, "restore") < restoreEffect
            )
            {
                bestPotionThatCanBeCrafted = item;
            }
        }

        return bestPotionThatCanBeCrafted;
    }
}
