using System.Collections.Immutable;
using System.Collections.Specialized;
using System.ComponentModel;
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
    RestockFoodParams JobParams { get; init; }

    public RestockFood(PlayerCharacter playerCharacter, GameState gameState, ChorePriority priority)
        : base(playerCharacter, gameState)
    {
        JobParams = GetJobParams(priority);
    }

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
        var bankResponse = await gameState.BankItemCache.GetBankItems(Character);

        // var jobsToCookUncookedResources = await GetListToCookAllUncookedMeatOrFish(
        //     bankResponse.Data
        // );

        // if (jobsToCookUncookedResources.Count > 0)
        // {
        //     return jobsToCookUncookedResources;
        // }

        var charactersToObtainFoodFor = GetCharactersToObtainFoodFor(
            gameState.Characters,
            gameState,
            bankResponse.Data,
            JobParams
        );

        if (charactersToObtainFoodFor.Count == 0)
        {
            return [];
        }

        List<ItemSchema> bestFoodItems =
        [
            .. charactersToObtainFoodFor.Select(recipientCharacter =>
                GetIdealFoodForCharacter(Character, recipientCharacter, gameState)
            ),
        ];

        // We want to prioritize high level items first, so the highest lvl chars get food.
        // The issue is that if making the low level food, the high lvl characters will also eat it, but they need it also
        bestFoodItems.Sort((a, b) => b.Level - a.Level);

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
                Character.GetAvailableInventorySpace(),
                JobParams.AmountToGather
            );

            var matchInBank = bestFoodItemsInBank.GetValueOrDefault(item.Code);

            if (matchInBank is not null && matchInBank.Quantity >= JobParams.MinimumAmountInBank)
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
        List<ItemSchema> foodCandidates =
        [
            .. gameState.Items.Where(item =>
            {
                return ItemService.IsItemCookedFish(item, gameState)
                    && ItemService.CanUseItem(item, character.Schema)
                    && crafter.Schema.CookingLevel >= item.Craft?.Level
                    && item.Craft.Items.Count == 1;
            }),
        ];

        // Assume higher lvl food gives more HP
        foodCandidates.Sort((a, b) => b.Level - a.Level);

        return foodCandidates.ElementAt(0);
    }

    public async Task<bool> NeedsToBeDone()
    {
        var jobs = await GetJobs();

        return jobs.Count > 0;
    }

    static RestockFoodParams GetJobParams(ChorePriority priority)
    {
        return priority switch
        {
            ChorePriority.Low => new RestockFoodParams
            {
                MinimumAmountInBank = 400,
                AmountToGather = 50,
            },
            ChorePriority.High => new RestockFoodParams
            {
                MinimumAmountInBank = 150,
                AmountToGather = 50,
            },
            _ => throw new NotImplementedException(),
        };
    }

    async Task<List<CharacterJob>> GetListToCookAllUncookedMeatOrFish(List<DropSchema> itemsInBank)
    {
        List<(DropSchema, ItemSchema)> uncookedMeatOrFishInBank =
        [
            .. itemsInBank
                .Select(item =>
                {
                    var matchingItem = gameState.ItemsDict[item.Code];
                    return (item, matchingItem);
                })
                .Where(item =>
                {
                    return IsItemUncookedMeatOrFish(item.matchingItem, gameState);
                })
                .Select(item =>
                {
                    // For now, we always assume that if item is uncooked meat or fish, there should be a recipe with only 1 ingredient.
                    var cookedItem = gameState
                        .CraftingLookupDict.GetValueOrNull(item.Item2.Code)!
                        .First(recipe => recipe!.Craft!.Items.Count == 1);

                    return (item.item, cookedItem);
                })
                .Where((item) => Character.Schema.CookingLevel >= item.cookedItem.Craft!.Level),
        ];

        List<CharacterJob> jobs =
            uncookedMeatOrFishInBank
                .Select(lol =>
                {
                    (DropSchema drop, ItemSchema item) = lol;

                    List<int> iterations = ObtainItem.CalculateObtainItemIterations(
                        item,
                        Character.GetAvailableInventorySpace(),
                        JobParams.AmountToGather
                    );

                    List<CharacterJob> jobs = [];

                    foreach (var iteration in iterations)
                    {
                        var job = new ObtainItem(Character, gameState, item.Code, iteration);

                        job.ForBank();

                        jobs.Add(job);
                    }

                    return jobs;
                })
                .FirstOrDefault() ?? [];

        List<CharacterJob> possibleJobs = [];

        foreach (var job in jobs)
        {
            if (
                await Character.PlayerActionService.CanObtainItem(
                    gameState.ItemsDict[job.Code],
                    job.Amount
                )
            )
            {
                possibleJobs.Add(job);
            }
        }

        return possibleJobs;
    }

    static bool IsItemUncookedMeatOrFish(ItemSchema item, GameState gameState)
    {
        if (item.Type != "resource")
        {
            return false;
        }

        var itemsThatCanBeCrafted = gameState.CraftingLookupDict.GetValueOrNull(item.Code) ?? [];

        return itemsThatCanBeCrafted.All(craftedItem => craftedItem.Type == "consumable")
            && itemsThatCanBeCrafted.Exists(craftedItem =>
                ItemService.IsItemCookedFish(craftedItem, gameState)
                || ItemService.IsItemCookedMeat(craftedItem, gameState)
            );
    }

    static List<PlayerCharacter> GetCharactersToObtainFoodFor(
        List<PlayerCharacter> characters,
        GameState gameState,
        List<DropSchema> itemsInBank,
        RestockFoodParams jobParams
    )
    {
        return
        [
            .. characters.Where(character =>
            {
                var goodEnoughFoodMatch = itemsInBank.Exists(item =>
                {
                    var matchingItem = gameState.ItemsDict[item.Code];

                    bool isFoodish =
                        (
                            matchingItem.Type == "consumable"
                            && matchingItem.Subtype == "food"
                            && matchingItem.Craft is not null
                            && ItemService.CanUseItem(matchingItem, character.Schema)
                        )
                        || (
                            IsItemUncookedMeatOrFish(matchingItem, gameState)
                            && matchingItem.Level > character.Schema.Level
                        );

                    /**
                    ** We want to verify that it's some kind of cooked food - it's OK if it's apple pies etc., and not meat or fish.
                    ** We want to eat all kinds of cooked food, if available. It's also okay that it's uncooked, cuz then we can cook it.
                    */
                    return isFoodish && item.Quantity >= jobParams.MinimumAmountInBank;
                });

                return !goodEnoughFoodMatch;
            }),
        ];
    }
}

public record RestockFoodParams
{
    public required int MinimumAmountInBank { get; init; }
    public required int AmountToGather { get; init; }
}
