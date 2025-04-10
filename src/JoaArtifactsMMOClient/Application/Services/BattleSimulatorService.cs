using Application.ArtifactsApi.Schemas;

namespace Applicaton.Services.FightSimulator;

public static class FightSimulatorService
{
    public static FightOutcome CalculateFightOutcome(
        CharacterSchema character,
        MonsterSchema monster
    )
    {
        // TODO: Implement
        return new FightOutcome
        {
            Result = FightResult.Win,
            PlayerHp = character.Hp,
            MonsterHp = 0,
            TotalTurns = 1,
        };
    }
}

public record FightOutcome
{
    public FightResult Result { get; init; }

    public int PlayerHp { get; init; }

    public int MonsterHp { get; init; }

    public int TotalTurns { get; init; }
}
