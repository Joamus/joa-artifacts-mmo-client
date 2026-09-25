using Application.ArtifactsApi.Schemas;
using Application.Character;
using Application.Dtos;
using Application.Errors;
using Application.Jobs.Chores;
using Application.Services;
using Applicaton.Jobs.Chores;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public class RestockPotions : CharacterJob, ICharacterChoreJob
{
    const int BASELINE_RESTOCK_TELEPORT_POTIONS_AMOUNT = 50;
    RestockPotionsParams JobParams { get; init; }

    public RestockPotions(
        PlayerCharacter playerCharacter,
        GameState gameState,
        ChorePriority priority,
        bool isRaidComingUpInSomeHours
    )
        : base(playerCharacter, gameState)
    {
        JobParams = GetJobParams(priority, isRaidComingUpInSomeHours);
    }

    protected override async Task<OneOf<AppError, None>> ExecuteAsync()
    {
        var jobs = await GetJobs();

        if (jobs.Count > 0)
        {
            var firstJob = jobs.First();

            // For now, just queue the first one, so we can also do other chores if needed etc.
            await Character.QueueJobsAfter(Id, [firstJob]);
        }

        return new None();
    }

    public async Task<List<ObtainItem>> GetJobs()
    {
        // Next season will make these both craftable and purchasable, depending on potion
        var levelRange = gameState.GetCharacterLevelRange();

        var bankItems = await gameState.Services.BankItemCache.GetBankItems(Character);

        var nextTeleportPotionToRestock = await GetNextTeleportPotionToRestock(
            gameState,
            bankItems,
            levelRange
        );

        if (nextTeleportPotionToRestock is not null)
        {
            return
            [
                new ObtainItem(
                    Character,
                    gameState,
                    nextTeleportPotionToRestock.Code,
                    nextTeleportPotionToRestock.Quantity
                ),
            ];
        }

        var bankResponse = await gameState.Services.BankItemCache.GetBankItems(Character);

        var bestPotions = await GetAllPotionCandidates();

        bestPotions =
        [
            .. bestPotions.OrderBy(
                (a) =>
                {
                    // Splash restore pots are currently something that we only restock here,
                    // so by prefering them, we can ensnure that we always have some stock,
                    // and the normal restore potions can be crafted by the characters when they need them.
                    if (a.Effects.Exists(effect => effect.Code == Effect.SplashRestore))
                    {
                        return 0;
                    }

                    if (IsRestorePotion(a))
                    {
                        return 1;
                    }

                    // Ugly, but it works
                    return 1000 - a.Level;
                }
            ),
        ];

        List<string> potionCodesWeHaveEnoughOf = [];

        foreach (var item in bankResponse)
        {
            if (string.IsNullOrWhiteSpace(item.Code))
            {
                continue;
            }

            var matchingItem = gameState.ItemsDict[item.Code];

            if (
                matchingItem.Type == "utility"
                && bestPotions.Exists(potion =>
                    potion.Code == item.Code
                    && !ShouldRestock(gameState.ItemsDict[potion.Code], item.Quantity)
                )
            )
            {
                potionCodesWeHaveEnoughOf.Add(item.Code);
            }
        }

        List<ObtainItem> jobs =
        [
            .. bestPotions
                .Where(potion => !potionCodesWeHaveEnoughOf.Contains(potion.Code))
                .Select(potion =>
                {
                    var job = new ObtainItem(Character, gameState, potion.Code, GetRestockAmount());

                    job.ForBank();

                    return job;
                }),
        ];

        return jobs;
    }

    async Task<List<ItemSchema>> GetAllPotionCandidates()
    {
        var potions = gameState.Items.Where(item => item.Type == "utility").ToList();

        potions.Sort((a, b) => b.Level - a.Level);

        Dictionary<string, ItemSchema> result = [];

        foreach (var character in gameState.Characters)
        {
            var usablePotions = potions
                .Where(item => ItemService.CanUseItem(item, character.Schema, gameState))
                .ToList();

            List<ItemSchema> potionsForCharacter = [];

            foreach (var potion in usablePotions)
            {
                // We only want 1 potion per effect, e.g. the highest level restore/boost potion we can get

                bool skipPotion = false;

                foreach (var existingPotion in potionsForCharacter)
                {
                    foreach (var existingEffect in existingPotion.Effects)
                    {
                        if (
                            potion.Effects.Exists(effect =>
                                effect.Code == existingEffect.Code
                                && existingEffect.Value > effect.Value
                            )
                        )
                        {
                            skipPotion = true;
                            break;
                        }
                    }
                    if (skipPotion)
                    {
                        break;
                    }
                }

                if (skipPotion)
                {
                    continue;
                }

                if (!await character.PlayerActionService.CanObtainItem(potion, 100, false))
                {
                    continue;
                }

                potionsForCharacter.Add(potion);
            }

            foreach (var potion in potionsForCharacter)
            {
                if (!result.ContainsKey(potion.Code))
                {
                    result.Add(potion.Code, potion);
                }
            }
        }

        return [.. result.Select(potion => potion.Value)];
    }

    public async Task<bool> NeedsToBeDone()
    {
        var jobs = await GetJobs();

        return jobs.Count > 0;
    }

    public bool IsRestorePotion(ItemSchema item)
    {
        return item.Effects.Exists(effect => effect.Code == "restore");
    }

    public int GetRestockAmount()
    {
        return JobParams.AmountToGather;
    }

    static RestockPotionsParams GetJobParams(ChorePriority priority, bool restockingForRaid)
    {
        var jobParams = priority switch
        {
            ChorePriority.Low => new RestockPotionsParams
            {
                MinimumAmountRestoreLikePotionsInBank = 400,
                MinimumAmountOtherPotionsInBank = 100,
                AmountToGather = 100,
            },
            ChorePriority.Medium => new RestockPotionsParams
            {
                MinimumAmountRestoreLikePotionsInBank = 300,
                MinimumAmountOtherPotionsInBank = 50,
                AmountToGather = 100,
            },
            ChorePriority.High => new RestockPotionsParams
            {
                MinimumAmountRestoreLikePotionsInBank = 200,
                MinimumAmountOtherPotionsInBank = 50,
                AmountToGather = 100,
            },
            _ => throw new NotImplementedException(),
        };

        int restockingForRaidFactor = restockingForRaid ? 2 : 1;

        return jobParams with
        {
            MinimumAmountRestoreLikePotionsInBank =
                jobParams.MinimumAmountRestoreLikePotionsInBank * restockingForRaidFactor,
            MinimumAmountOtherPotionsInBank =
                jobParams.MinimumAmountOtherPotionsInBank * restockingForRaidFactor,
        };
    }

    public bool ShouldRestock(ItemSchema item, int currentAmount)
    {
        bool isRestoreLikePotion =
            IsRestorePotion(item)
            || item.Effects.Exists(effect => effect.Code == Effect.SplashRestore);

        return currentAmount
            <= (
                isRestoreLikePotion
                    ? JobParams.MinimumAmountRestoreLikePotionsInBank
                    : JobParams.MinimumAmountOtherPotionsInBank
            );
    }

    public async Task<DropSchema?> GetNextTeleportPotionToRestock(
        GameState gameState,
        List<DropSchema> bankItems,
        LevelRange levelRange
    )
    {
        var bankItemsDict = bankItems.ToDictionary(item => item.Code);

        var highestLevelCharacter = gameState.Characters.First(character =>
            character.Schema.Level == levelRange.Highest
        );

        List<(ItemSchema item, DropSchema drop)> acquireableTeleportPotions = [];

        foreach (var item in gameState.Items)
        {
            if (
                ItemService.IsTeleportPotion(item)
                && ItemService.CanUseItem(item, highestLevelCharacter.Schema, gameState)
            )
            {
                int amountInBank = bankItemsDict.GetValueOrDefault(item.Code)?.Quantity ?? 0;

                int totalAmountWanted = GetAmountOfTeleportPotionsToRestock(levelRange.Highest);

                if (amountInBank < totalAmountWanted)
                {
                    int amountToObtain = Math.Max(
                        totalAmountWanted - amountInBank,
                        JobParams.AmountToGather
                    );

                    var canObtain = await Character.PlayerActionService.CanObtainItem(
                        item,
                        amountToObtain,
                        false
                    );

                    if (canObtain)
                    {
                        acquireableTeleportPotions.Add(
                            (item, new DropSchema { Code = item.Code, Quantity = amountToObtain })
                        );
                    }
                }
            }
        }

        acquireableTeleportPotions.Sort((a, b) => b.item.Level - a.item.Level);

        var bestCandidate = acquireableTeleportPotions.FirstOrDefault();

        foreach ((ItemSchema item, DropSchema drop) in acquireableTeleportPotions)
        {
            return drop;
        }

        return null;
    }

    public static int GetAmountOfTeleportPotionsToRestock(int maxCharacterLevel)
    {
        return Math.Max(
            BASELINE_RESTOCK_TELEPORT_POTIONS_AMOUNT,
            BASELINE_RESTOCK_TELEPORT_POTIONS_AMOUNT * maxCharacterLevel / 10
        );
    }
}

public record RestockPotionsParams
{
    public required int MinimumAmountRestoreLikePotionsInBank { get; init; }
    public required int MinimumAmountOtherPotionsInBank { get; init; }
    public required int AmountToGather { get; init; }
}
