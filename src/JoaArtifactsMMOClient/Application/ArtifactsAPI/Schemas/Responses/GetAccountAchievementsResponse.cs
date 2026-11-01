namespace Application.ArtifactsApi.Schemas.Responses;

public record GetAccountAchievementsResponse
{
    public required List<AccountAchievementSchema> Data { get; set; } = [];
}

public record AccountAchievementSchema
{
    public required string Code { get; set; }
    public required List<AccountAchievementObjectiveSchema> Objectives { get; set; } = [];
    public string? CompletedAt { get; set; }

    public required int Points { get; set; }
}

public record AccountAchievementObjectiveSchema
{
    public AchievementObjective Type { get; set; }
    public string? Target { get; set; }
    public required int Progress { get; set; }
    public required int Total { get; set; }
}
