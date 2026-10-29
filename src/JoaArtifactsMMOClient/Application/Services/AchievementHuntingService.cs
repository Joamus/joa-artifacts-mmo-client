using Application.ArtifactsApi.Schemas.Responses;
using Application.Character;
using Application.Jobs;
using OneOf.Types;

namespace Application.Services;

public class AchievementHuntingService
{
    public bool HasDoneItemTask { get; private set; }
    ILogger Logger { get; init; }

    GameState gameState { get; init; }
    const int CLEAN_UP_MINUTE_INTERVAL = 10;
    public readonly FifoSemaphore JobLock = new(1, 1);

    Dictionary<string, DateTime> AchievementHuntsRecentlyAssigned = [];

    public AchievementHuntingService(ILogger logger, GameState gameState)
    {
        Logger = logger;
        this.gameState = gameState;
    }

    public void FlushAchievementsAssignedCache()
    {
        AchievementHuntsRecentlyAssigned = AchievementHuntsRecentlyAssigned
            .Where(hunt =>
            {
                DateTime intervalAgo = DateTime.UtcNow.AddMinutes(-CLEAN_UP_MINUTE_INTERVAL);

                return hunt.Value < intervalAgo;
            })
            .ToDictionary();
    }

    public async Task<AchievementWithJob?> GetNextAchievementJobResult(PlayerCharacter character)
    {
        try
        {
            await JobLock.WaitAsync();

            var result = await InnerGetNextAchievementJobResult(character);

            if (result is not null)
            {
                AchievementHuntsRecentlyAssigned.TryAdd(result.Achievement.Code, DateTime.UtcNow);
            }

            return result;
        }
        finally
        {
            JobLock.Release();
        }
    }

    async Task<AchievementWithJob?> InnerGetNextAchievementJobResult(PlayerCharacter character)
    {
        List<AchievementWithProgress> nonCompletedAchievements = gameState
            .Services.AchievementService.Achievements.Select(achievement =>
            {
                if (AchievementHuntsRecentlyAssigned.ContainsKey(achievement.Code))
                {
                    return null;
                }

                var matchingAccountAchievement =
                    gameState.Services.AchievementService.AccountAchievementsDict.GetValueOrNull(
                        achievement.Code
                    );

                bool shouldComplete =
                    matchingAccountAchievement is null
                    || !gameState.Services.AchievementService.AchievementIsCompleted(
                        matchingAccountAchievement
                    );

                if (!shouldComplete)
                {
                    return null;
                }

                return new AchievementWithProgress
                {
                    Achievement = achievement,
                    AccountAchievement = matchingAccountAchievement,
                };
            })
            .OfType<AchievementWithProgress>()
            .ToList();

        foreach (var achievementWithProgress in nonCompletedAchievements)
        {
            foreach (var objective in achievementWithProgress.Achievement.Objectives)
            {
                var currentProgress =
                    achievementWithProgress
                        .AccountAchievement?.Objectives.FirstOrDefault(progressedObjective =>
                            progressedObjective.Target == objective.Target
                        )
                        ?.Progress ?? 0;

                int neededProgress = objective.Total - currentProgress;

                if (neededProgress > 0)
                {
                    var job = await GetJobFromAchievementObjective(
                        character,
                        gameState,
                        objective,
                        neededProgress
                    );

                    if (job is not null)
                    {
                        return new AchievementWithJob
                        {
                            Job = job,
                            Achievement = achievementWithProgress.Achievement,
                        };
                    }
                }
            }
        }

        return null;
    }

    static async Task<CharacterJob?> GetJobFromAchievementObjective(
        PlayerCharacter character,
        GameState gameState,
        AchievementObjectiveSchema objective,
        int neededProgress
    )
    {
        string? target = objective.Target;

        if (target is null)
        {
            return null;
        }

        switch (objective.Type)
        {
            case AchievementObjective.CombatKill:
                var monster = gameState.AvailableMonstersDict.GetValueOrNull(
                    objective.Target ?? ""
                );

                if (monster is not null)
                {
                    return await character.PlayerActionService.GetJobToFightMonster(
                        monster,
                        neededProgress
                    );
                }

                break;
            case AchievementObjective.CombatDrop:
            case AchievementObjective.Gathering:
            case AchievementObjective.Crafting:
            case AchievementObjective.NpcBuy:
                // We assume for now that the NpcBuy item objectives are only for items that cannot be acquired any other way
                return await GetJobForAcquiringItem(
                    character,
                    gameState,
                    objective,
                    neededProgress
                );
            case AchievementObjective.Use:
                var amountAvailable = await gameState.GetAmountOfItemFromAll(target);

                int actualNeeded = neededProgress - amountAvailable;

                if (actualNeeded > 0)
                {
                    return await GetJobForAcquiringItem(
                        character,
                        gameState,
                        objective,
                        actualNeeded
                    );
                }

                break;
            case AchievementObjective.NpcSell:
                break;
        }

        return null;
    }

    static async Task<CharacterJob?> GetJobForAcquiringItem(
        PlayerCharacter character,
        GameState gameState,
        AchievementObjectiveSchema objective,
        int neededProgress
    )
    {
        var matchingItem = gameState.ItemsDict.GetValueOrNull(objective.Target!);

        if (matchingItem is null)
        {
            return null;
        }

        if (
            await character.PlayerActionService.CanObtainItem(
                matchingItem,
                neededProgress,
                false,
                []
            )
        )
        {
            var obtainItemJob = ItemService.GetObtainOrCraftForJob(
                character,
                gameState,
                matchingItem,
                neededProgress
            );

            return obtainItemJob;
        }
        return null;
    }
}

public record AchievementWithProgress
{
    public required AchievementSchema Achievement { get; init; }
    public required AccountAchievementSchema? AccountAchievement { get; init; }
}

public record AchievementWithJob
{
    public required AchievementSchema Achievement { get; init; }
    public required CharacterJob Job { get; init; }
}
