using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Application.Jobs.Chores;

public record CharacterChore
{
    // [JsonConverter(typeof(StringEnumConverter))]
    public required CharacterChoreKind Kind { get; set; }

    // [JsonConverter(typeof(StringEnumConverter))]
    public required ChorePriority Priority { get; set; } = ChorePriority.High;

    public required DateTime StartedAt { get; set; }
    public required DateTime? CompletedAt { get; set; }
}

public enum ChorePriority
{
    Low,
    Medium,
    High,
}

public enum CharacterChoreKind
{
    RecycleUnusedItems,
    SellUnusedItems,
    RestockFood,
    RestockTasksCoins,
    RestockTasksCoinsOnlyFight,
    RestockPotions,
    RestockResources,
    GambleTasksCoins,
}
