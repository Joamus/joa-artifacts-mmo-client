using System.Threading.Tasks;
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

    public static readonly int POTION_BATCH_SIZE = 10;

    public ObtainSuitablePotions(PlayerCharacter playerCharacter, GameState gameState, int amount)
        : base(playerCharacter, gameState)
    {
        _amount = amount;
    }

    protected override async Task<OneOf<AppError, None>> ExecuteAsync()
    {
        logger.LogInformation(
            $"{JobName}: [{Character.Schema.Name}] run started - need to find {_amount} potions"
        );

        // If still not enough, we just go gather and cook some - be biased towards fishing, fastest way to get food

        var jobs = await GetAcquirePotionJobs(Character, gameState, GetPotionsToObtain(Character));

        logger.LogInformation(
            $"{JobName}: [{Character.Schema.Name}] found {jobs.Count} jobs - need to find {_amount} potions"
        );
        if (jobs.Count > 0)
        {
            Character.QueueJobsAfter(Id, jobs);
        }

        return new None();
    }

    public static int GetPotionsToObtain(PlayerCharacter character)
    {
        // We want to ensure that we don't fill our inventory
        int inventorySpaceLeft = character.GetInventorySpaceLeft();

        return Math.Max(PlayerActionService.MAX_AMOUNT_UTILITY_SLOT, inventorySpaceLeft / 2);
    }

    // private async Task<OneOf<AppError, List<CharacterJob>>> GetJobsToObtainPotions()
    // {
    //     var result = await gameState.AccountRequester.GetBankItems();

    //     if (result is not BankItemsResponse bankItemsResponse)
    //     {
    //         return new AppError("Failed to get bank items");
    //     }

    //     int amountFound = 0;

    //     List<CharacterJob> jobs = [];

    //     List<ItemInInventory> potionCandiates = [];

    //     foreach (var item in bankItemsResponse.Data)
    //     {
    //         var matchingItem = gameState.UtilityItemsDict.GetValueOrNull(item.Code);

    //         // Should not happen, but handle later maybe
    //         if (matchingItem is null || matchingItem.Subtype != "potion")
    //         {
    //             continue;
    //         }

    //         // int levelDifference = _playerCharacter._character.Level - matchingItem.Level;
    //         // If item is null, then it has been deleted from the game or something
    //         if (
    //             matchingItem.Effects.Find(effect => effect.Code == "restore") is not null
    //             && ItemService.CanUseItem(matchingItem, Character.Schema.Level)
    //         )
    //         {
    //             potionCandiates.Add(
    //                 new ItemInInventory { Item = matchingItem, Quantity = item.Quantity }
    //             );
    //         }
    //     }

    //     // We want to always use the best pots we can
    //     CalculationService.SortItemsBasedOnEffect(potionCandiates, "restore", false);

    //     foreach (var item in potionCandiates)
    //     {
    //         int amountToTake = Math.Min(_amount - amountFound, item.Quantity);

    //         jobs.Add(new WithdrawItem(Character, gameState, item.Item.Code, amountToTake));

    //         amountFound += Math.Min(_amount - amountFound, item.Quantity);

    //         if (amountFound >= _amount)
    //         {
    //             break;
    //         }
    //     }

    //     if (amountFound >= _amount)
    //     {
    //         return jobs;
    //     }

    //     var mostSuitablePotion = GetMostSuitablePotion(Character, gameState);

    //     if (mostSuitablePotion is not null)
    //     {
    //         jobs.Add(new ObtainItem(Character, gameState, mostSuitablePotion.Code, _amount));
    //     }

    //     return jobs;
    // }

    public static async Task<List<CharacterJob>> GetAcquirePotionJobs(
        PlayerCharacter character,
        GameState gameState,
        int preferedAmount
    )
    {
        List<(ItemSchema item, bool canCraft, int amountInBank)> potionCandidates = [];

        var bankItemsResponse = await gameState.AccountRequester.GetBankItems();

        foreach (var element in gameState.UtilityItemsDict)
        {
            var item = element.Value;
            int restoreEffect = ItemService.GetEffect(item, "restore");

            if (restoreEffect == 0)
            {
                continue;
            }

            if (!ItemService.CanUseItem(item, character.Schema))
            {
                continue;
            }

            var canCraftItem =
                item.Craft?.Skill == Skill.Alchemy
                && character.Schema.AlchemyLevel >= item.Craft.Level;

            int amountInBank =
                bankItemsResponse
                    .Data.FirstOrDefault(bankItem => bankItem.Code == item.Code)
                    ?.Quantity ?? 0;

            if (!canCraftItem && amountInBank == 0)
            {
                continue;
            }

            potionCandidates.Add((item, canCraftItem, amountInBank));
        }

        potionCandidates.Sort(
            (a, b) =>
                ItemService
                    .GetEffect(b.item, "restore")
                    .CompareTo(ItemService.GetEffect(a.item, "restore"))
        );

        var bestPotionCandidate = potionCandidates.ElementAtOrDefault(0);

        List<CharacterJob> resultJobs = [];

        int amountLeft = preferedAmount;

        foreach (var candidate in potionCandidates)
        {
            if (bestPotionCandidate.amountInBank > 0)
            {
                var amount = Math.Max(preferedAmount, bestPotionCandidate.amountInBank);

                var job = new WithdrawItem(
                    character,
                    gameState,
                    bestPotionCandidate.item.Code,
                    amount
                );

                amountLeft = amountLeft - amount;

                // Craft it or learn to craft it, if needed.
                job.CanTriggerObtain = true;
            }
            else
            {
                // split the jobs

                int ingredientsRequiredToCraftOne = 0;

                foreach (var material in candidate.item.Craft!.Items)
                {
                    ingredientsRequiredToCraftOne += material.Quantity;
                }

                int totalIngredientsRequired = ingredientsRequiredToCraftOne * amountLeft;

                int iterations = (int)
                    Math.Ceiling((double)totalIngredientsRequired / POTION_BATCH_SIZE);

                for (int i = 0; i < iterations; i++)
                {
                    int amountToCraft = Math.Min(amountLeft, POTION_BATCH_SIZE);

                    if (amountLeft <= 0)
                    {
                        // should not happen
                        break;
                    }
                    var job = new ObtainItem(
                        character,
                        gameState,
                        bestPotionCandidate.item.Code,
                        amountToCraft
                    );

                    amountLeft = amountLeft - POTION_BATCH_SIZE;
                }

                amountLeft = 0;
            }

            if (amountLeft <= 0)
            {
                break;
            }
        }
        return resultJobs;
    }
}
