using Application.ArtifactsApi.Schemas;
using Application.Character;
using Application.Errors;
using Application.Jobs.Chores;
using Applicaton.Jobs.Chores;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public class ObtainCuratedItems : CharacterJob, ICharacterChoreJob
{
    List<CuratedItem> CuratedItems { get; set; } =
    [
        new CuratedItem { Code = "healing_rune", Quantity = 5 },
        new CuratedItem { Code = "lifesteal_rune", Quantity = 3 },
        new CuratedItem { Code = "burn_rune", Quantity = 3 },
        new CuratedItem { Code = "protection_rune", Quantity = 1 },
        new CuratedItem { Code = "lost_world_map", Quantity = 5 },
        new CuratedItem { Code = "perfect_pearl", Quantity = 5 },
        // new CuratedItem { Code = "cultist_cloak", Quantity = 5 },
        new CuratedItem { Code = "healing_aura_rune", Quantity = 5 },
        new CuratedItem { Code = "vampiric_rune", Quantity = 5 },
        new CuratedItem
        {
            Code = "greater_healing_rune",
            Quantity = 5,
            Superseeds = ["healing_rune", "healing_aura_rune"],
        },
        new CuratedItem
        {
            Code = "greater_lifesteal_rune",
            Quantity = 3,
            Superseeds = ["lifesteal_rune"],
        },
        new CuratedItem
        {
            Code = "greater_protection_rune",
            Quantity = 1,

            Superseeds = ["protection_rune"],
        },
    ];
    GambleTasksCoinsParams JobParams { get; init; }

    public ObtainCuratedItems(
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
        logger.LogInformation(
            "{JobName}: [{Character.Schema.Name}] run started",
            JobName,
            Character.Schema.Name
        );

        var job = await GetJob();

        if (job is not null)
        {
            await Character.QueueJobsAfter(Id, [job]);
        }

        return new None();
    }

    public async Task<CharacterJob?> GetJob()
    {
        foreach (var curatedItem in CuratedItems)
        {
            var matchingItem = gameState.ItemsDict.GetValueOrNull(curatedItem.Code);

            if (matchingItem is null)
            {
                continue;
            }

            int amountNeeded = await GetAmountNeededOfCuratedItem(matchingItem, curatedItem);

            if (amountNeeded == 0)
            {
                continue;
            }

            var superSeededBy = CuratedItems.Where(otherCuratedItem =>
                otherCuratedItem.Code != curatedItem.Code
                && otherCuratedItem.Superseeds is not null
                && otherCuratedItem.Superseeds.Contains(curatedItem.Code)
            );

            bool haveAllWeNeed = false;

            // If we find any item that superseeds this item, and we have enough of that one, then we stop
            foreach (var superSeedingItem in superSeededBy)
            {
                bool weHaveEnoughOfThisItem =
                    await GetAmountNeededOfCuratedItem(matchingItem, curatedItem) == 0;

                if (weHaveEnoughOfThisItem)
                {
                    haveAllWeNeed = true;
                    break;
                }
            }

            // Even if we e.g. don't have the amount of lifesteal runes we want, then it's OK if we have
            if (haveAllWeNeed)
            {
                break;
            }

            // We only actually get one at a time, since in many cases we might not be able to afford multiple at a time.
            // For example, if we need 3 burn runes, then we should just get 1 at a time, since the `CanObtainItem` call will
            // return false, if we only have budget for e.g. 1 or 2.
            int amountToObtainAtOnce = 1;

            if (
                await Character.PlayerActionService.CanObtainItem(
                    matchingItem,
                    amountToObtainAtOnce,
                    false
                )
            )
            {
                return new ObtainItem(
                    Character,
                    gameState,
                    matchingItem.Code,
                    amountToObtainAtOnce,
                    []
                );
            }
        }

        return null;
    }

    async ValueTask<int> GetAmountNeededOfCuratedItem(
        ItemSchema matchingItem,
        CuratedItem curatedItem
    )
    {
        int amountOfItem = await gameState.GetAmountOfItemFromAll(matchingItem.Code);

        int amountNeeded = Math.Max(curatedItem.Quantity - amountOfItem, 0);

        return amountNeeded;
    }

    public async Task<bool> NeedsToBeDone()
    {
        var job = await GetJob();

        return job is not null;
    }

    static GambleTasksCoinsParams GetJobParams(ChorePriority priority)
    {
        return priority switch
        {
            _ => new GambleTasksCoinsParams
            {
                MinimumCoinsThreshold = 100,
                MinimumQuantityOfTaskItems = 10,
            },
        };
    }

    public bool IsAboveThreshold(int amountOfTasksCoins)
    {
        return amountOfTasksCoins > JobParams.MinimumCoinsThreshold;
    }
}

public record CuratedItem
{
    public required string Code { get; init; }
    public required int Quantity { get; init; }

    // This item is effectively a better replacement for other item(s)
    public List<string>? Superseeds { get; init; }
}
