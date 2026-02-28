using System.Collections.Immutable;
using Application.ArtifactsApi.Schemas;
using Application.ArtifactsApi.Schemas.Responses;
using Application.Character;
using Application.Errors;
using Application.Services;
using Applicaton.Jobs.Chores;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public class RestockResources : CharacterJob, ICharacterChoreJob
{
    const int LOWER_RESOURCE_THRESHOLD = 100;
    const int HIGHER_RESOURCE_THRESHOLD = 200;

    // We don't want to keep gathering resources to get
    const float RESTOCK_ITEM_DROP_RATE_THRESHOLD = 0.1f;

    public RestockResources(PlayerCharacter playerCharacter, GameState gameState)
        : base(playerCharacter, gameState) { }

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

        var relevantResources = GetRelevantResources(gameState, levelRange);

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

    public static List<ResourceSchema> GetRelevantResources(
        GameState gameState,
        LevelRange levelRange
    )
    {
        // The cheeky one is just to look at the level of the resource

        return gameState
            .Resources.Where(x => x.Level >= levelRange.Lowest && x.Level <= levelRange.Highest)
            .ToList();
    }

    public static List<DropSchema> GetNextItemToRestock(
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

                    var drops = gameState.DropItemsDict[code].ToList();
                    // We want the lowest number first, cuz 1 is 100% chance, 2 is 50%, etc.
                    drops.Sort((a, b) => a.Drop.Rate - b.Drop.Rate);

                    var bestDrop = drops.First();

                    int amountInBank = bankItemsDict.GetValueOrNull(code)?.Quantity ?? 0;

                    if (
                        !ShouldRestock(bestDrop.Drop, amountInBank)
                        || IsTooRareToRestock(bestDrop.Drop)
                    )
                    {
                        return new DropSchema { Code = code, Quantity = 0 };
                    }

                    return new DropSchema
                    {
                        Code = code,
                        Quantity = AmountToGather(CalculateDropRate(bestDrop.Drop.Rate)),
                    };
                })
                .Where(drop => drop.Quantity > 0),
        ];

        result.Sort(
            (a, b) => gameState.ItemsDict[b.Code].Level - gameState.ItemsDict[a.Code].Level
        );

        return result;

        // Get the items that are dropped which have a 1 drop rate - those are the primary goal of the gathering? Or maybe use a formula?
        // Return a list of the items we don't have enough of, compared to the bank items
        //
    }

    static bool ItemIsRelevantToRestock(ItemSchema item, LevelRange levelRange)
    {
        // Rough estimate, e.g. copper ore is relevant up to level 12ish - could be improved
        int minRelevantLevel = Math.Min(item.Level + 12, PlayerCharacter.MAX_LEVEL);

        return minRelevantLevel >= levelRange.Lowest && minRelevantLevel <= levelRange.Highest;
    }

    static int AmountToGather(float dropRate)
    {
        return (int)Math.Ceiling(HIGHER_RESOURCE_THRESHOLD * dropRate);
    }

    public static float CalculateDropRate(int dropRate)
    {
        return 1 / dropRate;
    }

    static bool ShouldRestock(DropRateSchema drop, int currentAmount)
    {
        var minimumAmount = (int)
            Math.Ceiling(LOWER_RESOURCE_THRESHOLD * CalculateDropRate(drop.Rate));

        return currentAmount < minimumAmount;
    }

    static bool IsTooRareToRestock(DropRateSchema drop)
    {
        return 1 / drop.Rate <= RESTOCK_ITEM_DROP_RATE_THRESHOLD;
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

    public async Task<bool> NeedsToBeDone()
    {
        return (await GetNextJob()) is not null;
    }
}

public struct LevelRange
{
    public int Lowest;
    public int Highest;
}
