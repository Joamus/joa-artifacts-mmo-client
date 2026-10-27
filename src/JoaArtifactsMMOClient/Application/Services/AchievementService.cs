using System.Collections;
using Application.ArtifactsApi.Schemas;
using Application.ArtifactsApi.Schemas.Responses;
using Application.Character;
using Application.Errors;
using Application.Records;
using Application.Services.ApiServices;

namespace Application.Services;

public class AchievementService
{
    public bool HasDoneItemTask { get; private set; }
    public AccountRequester AccountRequester { get; init; }
    private readonly ILogger<AchievementService> Logger;
    private GameState GameState { get; set; }
    public List<AchievementSchema> Achievements { get; set; } = [];
    public List<AccountAchievementSchema> AccountAchievements { get; set; } = [];
    public Dictionary<string, AccountAchievementSchema> AccountAchievementsDict { get; set; } = [];

    public AchievementService(
        ILogger<AchievementService> logger,
        AccountRequester accountRequester,
        GameState gameState
    )
    {
        Logger = logger;
        AccountRequester = accountRequester;
        GameState = gameState;
    }

    public async Task LoadAccountAchievements()
    {
        Logger.LogInformation("Loading account achievements...");
        bool doneLoading = false;
        List<AccountAchievementSchema> accountAchievements = [];
        int pageNumber = 1;

        try
        {
            while (!doneLoading)
            {
                var result = await AccountRequester.GetAccountAchievements(pageNumber);

                foreach (var achievement in result.Data)
                {
                    accountAchievements.Add(achievement);
                }

                if (result.Data.Count == 0)
                {
                    doneLoading = true;
                }

                pageNumber++;
            }
            AccountAchievements = accountAchievements;
            AccountAchievementsDict = accountAchievements.ToDictionary(
                (achievement) => achievement.Code
            );

            HasDoneItemTask =
                AccountAchievementsDict.GetValueOrNull("tasks_farmer")?.CompletedAt is not null;

            Logger.LogInformation("Loading account achievements - DONE;");
        }
        catch (Exception e)
        {
            Logger.LogError(e.ToString());
        }
    }

    public async Task LoadAchievements()
    {
        Logger.LogInformation("Loading achievements...");
        bool doneLoading = false;
        List<AchievementSchema> achievements = [];
        int pageNumber = 1;

        try
        {
            while (!doneLoading)
            {
                var result = await AccountRequester.GetAchievements(pageNumber);

                foreach (var achievement in result.Data)
                {
                    achievements.Add(achievement);
                }

                if (result.Data.Count == 0)
                {
                    doneLoading = true;
                }

                pageNumber++;
            }

            Achievements = achievements;
            Logger.LogInformation("Loading achievements - DONE;");
        }
        catch (Exception e)
        {
            Logger.LogError(e.ToString());
        }
    }

    public bool AchievementIsCompleted(AccountAchievementSchema achievementSchema) =>
        !string.IsNullOrWhiteSpace(achievementSchema.CompletedAt);
}
