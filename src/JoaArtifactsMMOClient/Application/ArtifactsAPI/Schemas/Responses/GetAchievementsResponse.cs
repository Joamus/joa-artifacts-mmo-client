namespace Application.ArtifactsApi.Schemas.Responses;

public record GetAchievementsResponse
{
    public required List<AchievementSchema> Data { get; set; } = [];
}

public record AchievementSchema
{
    public required string Name { get; set; } = "";
    public required string Code { get; set; } = "";
    public required string Type { get; set; } = "";
}
