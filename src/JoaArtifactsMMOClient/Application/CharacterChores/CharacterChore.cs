using Application.Character;

public record CharacterChore
{
    public required PlayerCharacter Actor { get; set; }

    public required CharacterChoreKind Kind { get; set; }

    public required DateTime StartedAt { get; set; }
    public required DateTime? CompletedAt { get; set; }
}

public enum CharacterChoreKind
{
    RecycleUnusedItems,
    SellUnusedItems,
    RestockFood,
    RestockTasksCoins,
    RestockPotions,
}
