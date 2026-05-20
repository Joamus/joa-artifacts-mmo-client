using System.Collections.Immutable;
using Application.ArtifactsApi.Schemas;
using Application.ArtifactsApi.Schemas.Responses;
using Application.Character;
using Application.Dtos;
using Application.Errors;
using Application.Services;
using Applicaton.Jobs.Chores;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public class RestockResources : CharacterJob, ICharacterChoreJob
{
    const int RELEVANT_MIN_LEVEL_OFFSET = 12;

    // We don't want to keep gathering resources to get low drop rate items
    const float RESTOCK_ITEM_DROP_RATE_THRESHOLD = 0.1f;

    RestockResourcesParams JobParams { get; init; }

    public RestockResources(
        PlayerCharacter playerCharacter,
        GameState gameState,
        ChorePriority priority
    )
        : base(playerCharacter, gameState)
    {
        JobParams = GetJobParams(priority);
    }

    protected override async Task<OneOf<AppError, None>> ExecuteAsync()
    {
        var nextJob = await GetNextJob();

        if (nextJob is not null)
        {
            await Character.QueueJobsAfter(Id, [nextJob]);
        }

        return new None();
    }

    public async Task<CharacterJob?> GetNextJob()
    {
        var levelRange = GetCharacterLevelRange(gameState);

        var relevantResources = GetResourcesToConsider(gameState, levelRange);

        var bankItems = (await gameState.BankItemCache.GetBankItems(Character)).Data;

        var itemsToRestock = GetNextItemToRestock(gameState, bankItems, levelRange);

        List<CharacterJob> jobs =
        [
            .. itemsToRestock.Select(item => new GatherResourceItem(
                Character,
                gameState,
                item.Code,
                item.Quantity,
                false
            )),
        ];

        return jobs.FirstOrDefault();
    }

    public static List<ResourceSchema> GetResourcesToConsider(
        GameState gameState,
        LevelRange levelRange
    )
    {
        // The cheeky one is just to look at the level of the resource

        return [.. gameState.Resources.Where(x => x.Level <= levelRange.Highest)];
    }

    public List<DropSchema> GetNextItemToRestock(
        GameState gameState,
        List<DropSchema> bankItems,
        LevelRange levelRange
    )
    {
        var bankItemsDict = bankItems.ToDictionary(item => item.Code);

        List<DropSchema> result =
        [
            .. gameState
                .CraftingLookupDict.AsEnumerable()
                .Select(pair =>
                {
                    string code = pair.Key;

                    var craftsInto = pair.Value ?? [];

                    var matchingItem = gameState.ItemsDict[pair.Key];

                    bool relevantResource =
                        matchingItem.Type == "resource"
                        && ItemIsRelevantToRestock(matchingItem, levelRange)
                        // && matchingItem.Level >= levelRange.Lowest
                        // && matchingItem.Level <= levelRange.Highest
                        // We could do alchemy and fishing too, but those are covered by restocking food and potions
                        && (
                            matchingItem.Subtype == "woodcutting"
                            || matchingItem.Subtype == "mining"
                        );

                    if (!relevantResource)
                    {
                        return new DropSchema { Code = code, Quantity = 0 };
                    }

                    // We want the lowest number first, cuz 1 is 100% chance, 2 is 50%, etc.
                    var drops = gameState
                        .DropItemsDict[code]
                        .Where(drop =>
                            !gameState.EventService.IsEntityFromEvent(drop.Resource.Code)
                        )
                        .ToList();

                    drops.Sort((a, b) => a.Drop.Rate - b.Drop.Rate);

                    if (drops.Count == 0)
                    {
                        return new DropSchema { Code = code, Quantity = 0 };
                    }

                    var bestDrop = drops.First();

                    int amountInBank = bankItemsDict.GetValueOrNull(code)?.Quantity ?? 0;

                    if (
                        !ShouldRestock(matchingItem, bestDrop.Drop, amountInBank, levelRange)
                        || IsTooRareToRestock(bestDrop.Drop)
                        || gameState.EventService.IsEntityFromEvent(bestDrop.Resource.Code)
                    )
                    {
                        return new DropSchema { Code = code, Quantity = 0 };
                    }

                    return new DropSchema
                    {
                        Code = code,
                        Quantity = GetAmountToGather(CalculateDropRate(bestDrop.Drop.Rate)),
                    };
                })
                .Where(drop => drop.Quantity > 0),
        ];

        result.Sort(
            (a, b) => gameState.ItemsDict[b.Code].Level - gameState.ItemsDict[a.Code].Level
        );

        return result;
    }

    static bool ItemIsRelevantToRestock(ItemSchema item, LevelRange levelRange)
    {
        // Rough estimate, e.g. copper ore is relevant up to level 12ish - could be improved
        int minRelevantLevel = Math.Min(
            item.Level + RELEVANT_MIN_LEVEL_OFFSET,
            PlayerCharacter.MAX_LEVEL + 2
        );

        return minRelevantLevel >= levelRange.Lowest && item.Level <= levelRange.Highest;
    }

    int GetAmountToGather(float dropRate)
    {
        // We want to prioritize restocking high level items, and not necessarily restock as many low level items.
        // float levelPercentage = (float)item.Level / levelRange.Highest;

        // if (levelPercentage > 1)
        // {
        //     levelPercentage = 1;
        // }

        return (int)Math.Ceiling(JobParams.AmountToGather * dropRate);
    }

    public static float CalculateDropRate(int dropRate)
    {
        return 1 / dropRate;
    }

    bool ShouldRestock(
        ItemSchema item,
        DropRateSchema drop,
        int currentAmount,
        LevelRange levelRange
    )
    {
        // We want to prioritize restocking high level items, and not necessarily restock as many low level items.
        float levelPercentage = (float)item.Level / levelRange.Highest;

        if (levelPercentage > 1)
        {
            levelPercentage = 1;
        }
        var minimumAmount = (int)
            Math.Ceiling(
                JobParams.MinimumAmountInBank * CalculateDropRate(drop.Rate) * levelPercentage
            );

        return currentAmount < minimumAmount;
    }

    static bool IsTooRareToRestock(DropRateSchema drop)
    {
        return CalculateDropRate(drop.Rate) < RESTOCK_ITEM_DROP_RATE_THRESHOLD;
    }

    public static LevelRange GetCharacterLevelRange(GameState gameState)
    {
        List<int> characterLevels = gameState.Characters.Select((x) => x.Schema.Level).ToList();
        characterLevels.Sort((a, b) => a - b);

        return new LevelRange
        {
            Lowest = characterLevels.First(),
            Highest = characterLevels.Last(),
        };
    }

    static RestockResourcesParams GetJobParams(ChorePriority priority)
    {
        return priority switch
        {
            ChorePriority.Low => new RestockResourcesParams
            {
                MinimumAmountInBank = 800,
                AmountToGather = 100,
            },
            ChorePriority.High => new RestockResourcesParams
            {
                MinimumAmountInBank = 200,
                AmountToGather = 100,
            },
            _ => throw new NotImplementedException(),
        };
    }

    public async Task<bool> NeedsToBeDone()
    {
        return (await GetNextJob()) is not null;
    }
}

public record RestockResourcesParams
{
    public required int MinimumAmountInBank { get; init; }
    public required int AmountToGather { get; init; }
}
