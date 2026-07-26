namespace Application.ArtifactsApi.Schemas.Responses;

public record GetAchievementsResponse
{
    public required List<AchievementSchema> Data { get; set; } = [];
}

public record AchievementSchema
{
    public required string Name { get; set; } = "";
    public required string Code { get; set; } = "";
    public required int Points { get; set; }
    public required List<AchievementObjectiveSchema> Objectives { get; set; }
}

public record AchievementObjectiveSchema
{
    public required AchievementObjective Type { get; set; }
    public string? Target { get; set; }
    public required int Total { get; set; }
}

public enum AchievementObjective
{
    CombatKill,
    CombatDrop,
    CombatLevel,
    Gathering,
    Crafting,
    Recycling,
    Task,
    Other,
    Use,
    NpcBuy,
    NpcSell,
}
