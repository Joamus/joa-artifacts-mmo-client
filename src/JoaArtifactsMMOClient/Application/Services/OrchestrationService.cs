using Application.Character;

namespace Application.Services;

public class OrchestrationService
{
    GameState gameState { get; init; }

    public CharacterEvent? lastHouseKeeping { get; set; }

    public OrchestrationService(GameState gameState)
    {
        this.gameState = gameState;
    }
}

public record CharacterEvent
{
    public required DateTime dateTime { get; set; }

    public required PlayerCharacter playerCharacter { get; set; }
}
