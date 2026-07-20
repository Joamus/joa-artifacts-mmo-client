using System.Net;
using Application.ArtifactsApi.Schemas;
using Application.Character;
using Application.Errors;
using Applicaton.Services.FightSimulator;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public class FightBoss
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string JobName { get; private set; } = "";

    public const int CHARACTERS_IN_BOSS_FIGHT = 3;
    public FightBossStatus Status = FightBossStatus.New;

    public DateTime CreatedAt = DateTime.UtcNow;
    public DateTime? LastFight = null;

    public required PlayerCharacter Character { get; set; }
    public required GameState GameState { get; set; }
    public required List<PlayerCharacter> OtherCharacters { get; set; }
    public required List<PlayerCharacter> AllCharacters { get; set; }

    public required List<FightSimResult> FightSimResults { get; set; }

    public async Task<FightBoss> BuildFightBossJob(
        PlayerCharacter character,
        GameState gameState,
        List<PlayerCharacter> otherCharacters,
        MonsterSchema monster
    )
    {
        return new FightBoss
        {
            Character = character,
            GameState = gameState,
            OtherCharacters = otherCharacters,

            AllCharacters = [character, .. otherCharacters],

            FightSimResults = FightSimulator.SimulateBossFightOutcome(
                character,
                otherCharacters,
                gameState,
                await gameState.BankItemCache.GetBankItems(character),
                monster
            ),
        };
    }

    // public Task Setup()
    // {
    //     FightSimResult = FightSimulator.SimulateBossFightOutcome(character, otherCharacters, gameState, )
    // }

    // public Task<OneOf<AppError, CharacterJob?>> GetNextJob(PlayerCharacter character) { }

    public static List<PlayerCharacter> GetBestCandidatesToFight(
        PlayerCharacter character,
        GameState gameState
    )
    {
        List<PlayerCharacter> otherAvailablePlayers =
        [
            .. gameState.Characters.Where(otherCharacter =>
                otherCharacter.Schema.Name != character.Schema.Name
                && (otherCharacter.CurrentFightBossJob is null)
            ),
        ];

        otherAvailablePlayers.Sort((a, b) => b.Schema.Level - a.Schema.Level);

        int amountToRecruit = CHARACTERS_IN_BOSS_FIGHT - 1;

        return otherAvailablePlayers.GetRange(0, amountToRecruit);
    }

    public static void FightSimBoss(
        PlayerCharacter character,
        List<PlayerCharacter> otherCharacters,
        GameState gameState,
        MonsterSchema monster
    )
    {
        var outcome = FightSimulator.CalculateFightOutcome(
            character.Schema,
            otherCharacters.Select(player => player.Schema).ToList(),
            monster,
            gameState
        );
    }

    public bool ShouldStop()
    {
        // Stop the job after a timeout
        return false;
    }
}

public enum FightBossStatus
{
    New,
    Preparing,
    Fighting,
}
