namespace Application.ArtifactsApi.Schemas.Responses;

public record GetAccountAchievementsResponse
{
    public required List<AccountAchievementSchema> Data { get; set; } = [];
}

public record AccountAchievementSchema : AchievementSchema
{
    public required int Current { get; set; }
    public string? CompletedAt { get; set; }
}
