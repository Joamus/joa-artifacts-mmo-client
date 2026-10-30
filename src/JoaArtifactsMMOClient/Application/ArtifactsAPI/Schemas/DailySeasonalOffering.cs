using Application.Artifacts.Schemas;

namespace Application.ArtifactsApi.Schemas;

public record DailySeasonalOfferingSchema
{
    public required DateTime Date { get; set; }
    public required DateTime ExpiresAt { get; set; }
    public required List<Skill> Skills { get; set; }
    public required int XpRewardPercent { get; set; } = 25;
    public required List<SeasonalOfferingSchema> Offerings { get; set; }
}

public record SeasonalOfferingSchema
{
    public required string Id { get; set; }
    public required SeasonalOfferingCategory Category { get; set; }
    public required SeasonalOfferingType Type { get; set; }
    public required int LevelMin { get; set; }
    public required int LevelMax { get; set; }
    public required OfferingRequirementSchema Requirement { get; set; }
    public required OfferingFavorSchema Favor { get; set; }
}

public enum SeasonalOfferingCategory
{
    Combat,
    Mining,
    Woodcutting,
    Fishing,
    Alchemy,
    Weaponcrafting,
    Gearcrafting,
    Jewelrycrafting,
    Cooking,
}

public enum SeasonalOfferingType
{
    Monster,
    Resource,
    Craft,
}

public record OfferingRequirementSchema
{
    public required string Code { get; set; }
    public required int Quantity { get; set; }
}

public record OfferingFavorSchema
{
    public required SeasonalOfferingType TargetType { get; set; }
    public required string TargetCode { get; set; }
    public required int XpMultiplier { get; set; }
    public required int DropMultiplier { get; set; }
}
