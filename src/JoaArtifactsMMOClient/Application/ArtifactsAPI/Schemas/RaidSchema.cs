namespace Application.ArtifactsApi.Schemas;

public record RaidSchema
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public required string Monster { get; set; }
    public required RaidScheduleSchema Schedule { get; set; }
    public required RaidInstanceSchema? ActiveInstance { get; set; }
    public required RaidRewardsSchema Rewards { get; set; }
    public required RaidStatus Status { get; set; }
    public required DateTime NextStartAt { get; set; }

    public List<SimpleEffectSchema> Effects { get; set; } = [];

    public int MinGold { get; set; }

    public int MaxGold { get; set; }

    public List<DropRateSchema> Drops { get; set; } = [];
}

public record RaidScheduleSchema
{
    // public required List<string> Weekdays = [];
    public required int StartHourUtc { get; set; }
    public required int StartMinuteUtc { get; set; }
    public required int DurationHours { get; set; }
}

public record RaidRewardsSchema
{
    public required List<RaidDamageRewardsSchema> DamageRewards { get; set; }
}

public record RaidDamageRewardsSchema
{
    public required List<SimpleItemSchema> Items { get; set; }
    public required int DamagePerReward { get; set; }
    public required int MaxRewards { get; set; }
}

public enum RaidStatus
{
    Upcoming,
    Active,
    FinishedSuccess,
    FinishedFailure,
}

public record RaidInstanceSchema
{
    public required DateTime StartsAt { get; set; }
    public required DateTime EndsAt { get; set; }
    public required RaidStatus Status { get; set; }
    public required int TotalHp { get; set; }
    public required int RemainingHp { get; set; }
}
