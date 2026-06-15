using Application.ArtifactsApi.Schemas;
using Application.Character;
using Application.Errors;
using Application.Services;
using Applicaton.Jobs.Chores;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public class RestockPotions : CharacterJob, ICharacterChoreJob
{
    RestockPotionsParams JobParams { get; init; }

    public RestockPotions(
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
        var bankResponse = await gameState.BankItemCache.GetBankItems(Character);

        var bestPotions = await GetAllPotionCandidates();

        bestPotions.Sort(
            (a, b) =>
            {
                int aWinsValue = -1;
                int bWinsValue = 1;

                bool aIsRestoreHpPot = IsRestorePotion(a);
                bool bIsRestoreHpPot = IsRestorePotion(b);

                if (aIsRestoreHpPot && bIsRestoreHpPot)
                {
                    return b.Level - a.Level;
                }
                else if (aIsRestoreHpPot)
                {
                    return aWinsValue;
                }
                else if (bIsRestoreHpPot)
                {
                    return bWinsValue;
                }

                return b.Level - a.Level;
            }
        );

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
                .Where(item => ItemService.CanUseItem(item, character.Schema))
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

                var obtainItemResult = await ObtainItem.GetJobsRequired(
                    character,
                    gameState,
                    true,
                    potion.Code,
                    100,
                    true,
                    true,
                    true
                );

                switch (obtainItemResult.Value)
                {
                    case AppError error:
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

    static RestockPotionsParams GetJobParams(ChorePriority priority)
    {
        return priority switch
        {
            ChorePriority.Low => new RestockPotionsParams
            {
                MinimumAmountRestorePotionsInBank = 500,
                MinimumAmountOtherPotionsInBank = 50,
                AmountToGather = 30,
            },
            ChorePriority.High => new RestockPotionsParams
            {
                MinimumAmountRestorePotionsInBank = 200,
                MinimumAmountOtherPotionsInBank = 50,
                AmountToGather = 30,
            },
            _ => throw new NotImplementedException(),
        };
    }

    public bool ShouldRestock(ItemSchema item, int currentAmount)
    {
        bool isRestorePotion = IsRestorePotion(item);

        return currentAmount
            <= (
                isRestorePotion
                    ? JobParams.MinimumAmountRestorePotionsInBank
                    : JobParams.MinimumAmountOtherPotionsInBank
            );
    }
}

public record RestockPotionsParams
{
    public required int MinimumAmountRestorePotionsInBank { get; init; }
    public required int MinimumAmountOtherPotionsInBank { get; init; }
    public required int AmountToGather { get; init; }
}
