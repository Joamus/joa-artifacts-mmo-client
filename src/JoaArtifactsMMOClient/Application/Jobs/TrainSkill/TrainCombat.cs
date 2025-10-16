using Application.ArtifactsApi.Schemas;
using Application.Character;
using Application.Errors;
using Applicaton.Services.FightSimulator;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public class TrainCombat : CharacterJob
{
    public static readonly int AMOUNT_TO_KILL = 50;
    public int LevelOffset { get; private set; }
    public bool Relative { get; init; }

    public int PlayerLevel { get; set; }

    public TrainCombat(
        PlayerCharacter character,
        GameState gameState,
        int level,
        bool relative = false
    )
        : base(character, gameState)
    {
        LevelOffset = level;
        Relative = relative;
    }

    protected override async Task<OneOf<AppError, None>> ExecuteAsync()
    {
        // Only runs the first time this job runs. If it queues a job before itself, it shouldn't recalculate the level
        if (PlayerLevel == 0)
        {
            PlayerLevel = Character.Schema.Level;
        }

        int untilLevel;

        if (Relative)
        {
            untilLevel = PlayerLevel + LevelOffset;
        }
        else
        {
            untilLevel = LevelOffset;
        }

        logger.LogInformation(
            $"{JobName}: [{Character.Schema.Name}] run started - training combat until level {untilLevel}"
        );

        if (PlayerLevel < untilLevel)
        {
            var result = await GetJobRequired(PlayerLevel);

            switch (result.Value)
            {
                case CharacterJob job:
                    Character.QueueJobsBefore(Id, [job]);
                    Status = JobStatus.Suspend;
                    break;
                case AppError error:
                    return error;
            }
        }

        return new None();
    }

    public async Task<OneOf<AppError, CharacterJob>> GetJobRequired(int playerLevel)
    {
        OutcomeCandidate? bestMonsterCandidate = null;

        foreach (var monster in gameState.Monsters)
        {
            // Our character might be able to punch above their weight
            if (playerLevel > monster.Level + 10 || playerLevel + 5 < monster.Level)
            {
                continue;
            }

            int levelDifference = playerLevel - monster.Level;

            var outcome = FightSimulator.CalculateFightOutcomeWithBestEquipment(
                Character,
                monster,
                gameState
            );

            var candidate = new OutcomeCandidate
            {
                FightOutcome = outcome,
                MonsterCode = monster.Code,
                LevelDifference = levelDifference,
                MonsterLevel = monster.Level,
            };

            if (outcome.ShouldFight)
            {
                if (bestMonsterCandidate is null)
                {
                    bestMonsterCandidate = candidate;
                    continue;
                }

                // We always want to prioritize fighting monsters as close to the character's level as possible, to avoid an XP penalty.

                if (candidate.MonsterLevel > bestMonsterCandidate.MonsterLevel)
                {
                    // if (
                    //     candidate.FightOutcome.TotalTurns
                    //     <= bestMonsterCandidate.FightOutcome.TotalTurns
                    // )
                    // {
                    bestMonsterCandidate = candidate;
                    // }
                }
            }
        }

        if (bestMonsterCandidate is null)
        {
            return new AppError(
                $"{JobName}: [{Character.Schema.Name}]: error - no monste candidates to fight that give XP."
            );
        }

        return new FightMonster(
            Character,
            gameState,
            bestMonsterCandidate.MonsterCode,
            AMOUNT_TO_KILL
        );
    }
}

record OutcomeCandidate
{
    public required FightOutcome FightOutcome;
    public required string MonsterCode;
    public required int LevelDifference;
    public required int MonsterLevel;
}
