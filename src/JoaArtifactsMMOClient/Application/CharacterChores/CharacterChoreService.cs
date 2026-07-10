using Application.Character;
using Application.Jobs.Chores;

namespace Application.Services;

public class CharacterChoreService
{
    public CharacterChoreService() { }

    public const int MINUTES_BETWEEN_CHORES = 60;

    private Dictionary<CharacterChoreKind, CharacterChoreEntry> lastChores = [];

    public bool ShouldChoreBeStarted(CharacterChoreKind choreKind)
    {
        var existingChore = lastChores.GetValueOrNull(choreKind);

        return existingChore is null
            || existingChore?.CompletedAt is null
            || existingChore.CompletedAt < DateTime.UtcNow.AddMinutes(-MINUTES_BETWEEN_CHORES);
    }

    public bool HasOngoingChore(CharacterChoreKind choreKind)
    {
        return lastChores.GetValueOrNull(choreKind) is not null;
    }

    public void StartChore(PlayerCharacter character, CharacterChoreKind choreKind)
    {
        lastChores.Remove(choreKind);

        lastChores.Add(
            choreKind,
            new CharacterChoreEntry
            {
                Actor = character,
                StartedAt = DateTime.UtcNow,
                CompletedAt = null,
                Kind = choreKind,
            }
        );
    }

    public void FinishChore(CharacterChoreKind choreKind)
    {
        var existingChore = lastChores.GetValueOrNull(choreKind);

        if (existingChore is null)
        {
            return;
        }

        existingChore.CompletedAt = DateTime.UtcNow;
    }
}

public record CharacterChoreEntry
{
    public required PlayerCharacter Actor { get; set; }

    public required CharacterChoreKind Kind { get; set; }

    public required DateTime StartedAt { get; set; }
    public required DateTime? CompletedAt { get; set; }
}
