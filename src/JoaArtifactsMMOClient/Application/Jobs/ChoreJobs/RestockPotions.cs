using Application.ArtifactsApi.Schemas;
using Application.Character;
using Application.Errors;
using Application.Services;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public class RestockPotions : CharacterJob
{
    const int LOWER_POTION_THRESHOLD = 20;
    const int HIGHER_POTION_THRESHOLD = 200;

    public RestockPotions(PlayerCharacter playerCharacter, GameState gameState)
        : base(playerCharacter, gameState) { }

    protected override async Task<OneOf<AppError, None>> ExecuteAsync()
    {
        // Get the best effect per character, and queue x amount of those. Maybe a blacklist, to not get potions requiring event items?
        List<ObtainItem> jobs = await GetJobs();

        if (jobs.Count > 0)
        {
            await Character.QueueJobsAfter(Id, jobs);
        }
        return new None();
    }

    public async Task<List<ObtainItem>> GetJobs()
    {
        var bankResponse = await gameState.BankItemCache.GetBankItems(Character);

        var bestPotions = GetAllPotionCandidates();

        List<string> potionCodesWeHaveEnoughOf = [];

        var potionsInBank = bankResponse.Data;

        foreach (var item in potionsInBank)
        {
            if (string.IsNullOrWhiteSpace(item.Code))
            {
                continue;
            }

            var matchingItem = gameState.ItemsDict[item.Code];

            if (
                matchingItem.Type == "utility"
                && bestPotions.Exists(potion =>
                    potion.Code == item.Code && item.Quantity >= LOWER_POTION_THRESHOLD
                )
            )
            {
                potionCodesWeHaveEnoughOf.Add(item.Code);
            }
        }

        return bestPotions
            .Where(potion => !potionCodesWeHaveEnoughOf.Contains(potion.Code))
            .Select(potion =>
            {
                var job = new ObtainItem(
                    Character,
                    gameState,
                    potion.Code,
                    HIGHER_POTION_THRESHOLD
                );

                job.ForBank();

                return job;
            })
            .ToList();
    }

    List<ItemSchema> GetAllPotionCandidates()
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

        return result.Select(potion => potion.Value).ToList();
    }
}
