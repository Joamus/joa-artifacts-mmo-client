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

        return Math.Min(PlayerActionService.MAX_AMOUNT_UTILITY_SLOT, inventorySpaceLeft / 2);
    }

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
                    ?.Quantity
                ?? 0;

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
                resultJobs.Add(job);

                // Craft it or learn to craft it, if needed.
                job.CanTriggerObtain = true;
            }
            else
            {
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
                        break;
                    }
                    var job = new ObtainItem(
                        character,
                        gameState,
                        bestPotionCandidate.item.Code,
                        amountToCraft
                    );
                    job.AllowUsingMaterialsFromBank = true;

                    amountLeft = amountLeft - amountToCraft;
                    resultJobs.Add(job);
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
