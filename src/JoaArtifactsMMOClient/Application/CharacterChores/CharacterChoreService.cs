using Application.Character;
using Application.Jobs.Chores;

namespace Application.Services;

public class CharacterChoreService
{
    public CharacterChoreService() { }

    public const int MINUTES_BETWEEN_CHORES = 60;

    private Dictionary<CharacterChoreKind, CharacterChore> lastChores = [];

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
            new CharacterChore
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
