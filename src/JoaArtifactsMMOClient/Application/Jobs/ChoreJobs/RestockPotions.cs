using Application.Character;
using Application.Errors;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public class RestockPotions : CharacterJob
{
    const int LOWER_POTION_THRESHOLD = 10;
    const int HIGHER_POTION_THRESHOLD = 100;

    public RestockPotions(PlayerCharacter playerCharacter, GameState gameState)
        : base(playerCharacter, gameState) { }

    protected override async Task<OneOf<AppError, None>> ExecuteAsync()
    {
        // Get the best effect per character, and queue x amount of those. Maybe a blacklist, to not get potions requiring event items?
        return new None();
    }
}
