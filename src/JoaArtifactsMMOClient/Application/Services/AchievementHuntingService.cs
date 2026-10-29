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
    const int CLEAN_UP_MINUTE_INTERVAL = 60;
    public readonly FifoSemaphore JobLock = new(1, 1);

    Dictionary<string, AssignedAchievementHunt> AchievementHuntsRecentlyAssigned = [];

    public AchievementHuntingService(ILogger logger, GameState gameState)
    {
        Logger = logger;
        this.gameState = gameState;
    }

    bool IsOtherAssignedToObjective(
        AchievementSchema achievement,
        AchievementObjectiveSchema objective,
        PlayerCharacter character
    )
    {
        var entry = AchievementHuntsRecentlyAssigned.GetValueOrNull(
            GetRecentlyAssignedKey(achievement, objective)
        );

        return entry is not null && entry.CharacterName != character.Name;
    }

    void AssignWorkingOnObjective(
        AchievementSchema achievement,
        AchievementObjectiveSchema objective,
        PlayerCharacter character
    )
    {
        AchievementHuntsRecentlyAssigned.TryAdd(
            GetRecentlyAssignedKey(achievement, objective),
            new AssignedAchievementHunt
            {
                AssignedAt = DateTime.Now,
                CharacterName = character.Name,
            }
        );
    }

    string GetRecentlyAssignedKey(
        AchievementSchema achievement,
        AchievementObjectiveSchema objective
    )
    {
        return $"{achievement.Code}_${objective.Target}";
    }

    public void FlushAchievementsAssignedCache()
    {
        AchievementHuntsRecentlyAssigned = AchievementHuntsRecentlyAssigned
            .Where(hunt =>
            {
                DateTime intervalAgo = DateTime.UtcNow.AddMinutes(-CLEAN_UP_MINUTE_INTERVAL);

                return hunt.Value.AssignedAt < intervalAgo;
            })
            .ToDictionary();
    }

    public void RemoveAssignedAchievement(
        AchievementSchema achievement,
        AchievementObjectiveSchema objective,
        PlayerCharacter character
    )
    {
        var key = GetRecentlyAssignedKey(achievement, objective);

        var entry = AchievementHuntsRecentlyAssigned.GetValueOrNull(key);

        if (entry is not null && entry.CharacterName == character.Name)
        {
            AchievementHuntsRecentlyAssigned.Remove(key);
        }
    }

    public async Task<AchievementWithJobs?> GetNextAchievementJobResult(PlayerCharacter character)
    {
        try
        {
            await JobLock.WaitAsync();

            var result = await InnerGetNextAchievementJobResult(character);

            if (result is not null)
            {
                AssignWorkingOnObjective(result.Achievement, result.Objective, character);

                async Task onObjectiveEnd()
                {
                    RemoveAssignedAchievement(result.Achievement, result.Objective, character);
                }

                result.Job.onAfterSuccessEndHook = onObjectiveEnd;
                result.Job.onAfterFailHook = onObjectiveEnd;
            }

            return result;
        }
        finally
        {
            JobLock.Release();
        }
    }

    async Task<AchievementWithJobs?> InnerGetNextAchievementJobResult(PlayerCharacter character)
    {
        List<AchievementWithProgress> nonCompletedAchievements =
        [
            .. gameState
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
                .OfType<AchievementWithProgress>(),
        ];

        foreach (var achievementWithProgress in nonCompletedAchievements)
        {
            foreach (var objective in achievementWithProgress.Achievement.Objectives)
            {
                if (
                    IsOtherAssignedToObjective(
                        achievementWithProgress.Achievement,
                        objective,
                        character
                    )
                )
                {
                    continue;
                }

                var currentProgress =
                    achievementWithProgress
                        .AccountAchievement?.Objectives.FirstOrDefault(progressedObjective =>
                            progressedObjective.Target == objective.Target
                        )
                        ?.Progress ?? 0;

                int neededProgress = objective.Total - currentProgress;

                if (neededProgress > 0)
                {
                    var result = await GetJobFromAchievementObjective(
                        character,
                        gameState,
                        achievementWithProgress.Achievement,
                        objective,
                        neededProgress
                    );

                    if (result is not null)
                    {
                        return result;
                    }
                }
            }
        }

        return null;
    }

    static async Task<AchievementWithJobs?> GetJobFromAchievementObjective(
        PlayerCharacter character,
        GameState gameState,
        AchievementSchema achievement,
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
                    var monsterJob = await character.PlayerActionService.GetJobToFightMonster(
                        monster,
                        neededProgress
                    );

                    if (monsterJob is not null)
                    {
                        return new AchievementWithJobs
                        {
                            Job = monsterJob,
                            Achievement = achievement,
                            Objective = objective,
                        };
                    }
                }

                break;
            case AchievementObjective.CombatDrop:
            case AchievementObjective.Gathering:
            case AchievementObjective.Crafting:
            case AchievementObjective.NpcBuy:
                // We assume for now that the NpcBuy item objectives are only for items that cannot be acquired any other way
                var itemJob = await GetJobForAcquiringItem(
                    character,
                    gameState,
                    objective,
                    neededProgress
                );
                if (itemJob is not null)
                {
                    return new AchievementWithJobs
                    {
                        Job = itemJob,
                        Achievement = achievement,
                        Objective = objective,
                    };
                }
                break;
            case AchievementObjective.Use:
                var amountAvailable = await gameState.GetAmountOfItemFromAll(target);

                int actualNeeded = neededProgress - amountAvailable;

                // We don't actually use them straight away, we just obtain enough of it, and assume that we'll use it.
                if (actualNeeded > 0)
                {
                    var useJob = await GetJobForAcquiringItem(
                        character,
                        gameState,
                        objective,
                        actualNeeded
                    );

                    if (useJob is not null)
                    {
                        return new AchievementWithJobs
                        {
                            Job = useJob,
                            Achievement = achievement,
                            Objective = objective,
                        };
                    }
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
            // We just do the initial iteration every time
            List<int> iterations = ObtainItem.CalculateObtainItemIterations(
                matchingItem,
                character.GetAvailableInventorySpace(),
                neededProgress
            );

            int amountPossible = iterations.FirstOrDefault();

            if (amountPossible == 0)
            {
                amountPossible = neededProgress;
            }

            var job = ItemService.GetObtainOrCraftForJob(
                character,
                gameState,
                matchingItem,
                amountPossible
            );

            return job;
        }

        return null;
    }
}

public record AchievementWithProgress
{
    public required AchievementSchema Achievement { get; init; }
    public required AccountAchievementSchema? AccountAchievement { get; init; }
}

public record AchievementWithJobs
{
    public required AchievementSchema Achievement { get; init; }
    public required AchievementObjectiveSchema Objective { get; init; }
    public required CharacterJob Job { get; init; }
}

public record AssignedAchievementHunt
{
    public required DateTime AssignedAt { get; init; }
    public required string CharacterName { get; init; }
}
