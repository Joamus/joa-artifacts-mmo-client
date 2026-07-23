using Application.ArtifactsApi.Schemas;
using Application.Character;
using Application.Errors;
using Application.Records;
using Application.Services;
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
            var result = await GetJobRequired(Character, gameState, PlayerLevel);

            if (result is null)
            {
                return new AppError(
                    $"TrainCombat.GetJobRequired: [{Character.Schema.Name}]: error - no monster candidates to fight that give XP."
                );
            }

            await Character.QueueJobsBefore(Id, [result]);
            Status = JobStatus.Suspend;
        }

        return new None();
    }

    public static async Task<FightMonster?> GetJobRequired(
        PlayerCharacter character,
        GameState gameState,
        int playerLevel
    )
    {
        List<(FightOutcome Outcome, MonsterSchema Monster)> monsterCandidates = [];

        var bankItems = await FightSimulator.GetBankItemsForFightSim(character, gameState);

        foreach (var monster in gameState.AvailableMonsters)
        {
            // Our character might be able to punch above their weight
            if (
                playerLevel > monster.Level + PlayerActionService.LEVEL_DIFF_NO_XP
                || playerLevel + 5 < monster.Level
            )
            {
                continue;
            }

            var outcome = FightSimulator
                .FindBestFightEquipmentWithUsablePotions(character, gameState, monster, bankItems)
                .SimResult.Outcome;

            if (outcome.ShouldFight)
            {
                monsterCandidates.Add((outcome, monster));
            }
        }

        monsterCandidates.Sort(
            (a, b) =>
                GetKillMonsterScore(character, b.Monster, b.Outcome)
                - GetKillMonsterScore(character, a.Monster, a.Outcome)
        );

        var bestMonsterCandidate = monsterCandidates
            .Select(candidate => candidate.Monster)
            .FirstOrDefault();

        if (bestMonsterCandidate is null)
        {
            return null;
        }

        return new FightMonster(character, gameState, bestMonsterCandidate.Code, AMOUNT_TO_KILL);
    }

    static int GetKillMonsterScore(
        PlayerCharacter character,
        MonsterSchema monster,
        FightOutcome outcome
    )
    {
        int xpForFight = CalculationService.GetXpForFight(
            character.Schema,
            [character.Schema.Level],
            monster
        );

        float costForUsingPotions = outcome.PotionsUsed * 0.05f;

        float costForTurns = outcome.TotalTurns * 0.002f;

        float totalCostFactor = 1 + (costForUsingPotions + costForTurns);

        return (int)Math.Floor(xpForFight / totalCostFactor);
    }
}

record OutcomeCandidate
{
    public required FightOutcome FightOutcome;
    public required string MonsterCode;
    public required int LevelDifference;
    public required int MonsterLevel;
}
