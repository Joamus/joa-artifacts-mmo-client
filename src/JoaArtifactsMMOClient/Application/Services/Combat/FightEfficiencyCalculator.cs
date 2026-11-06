using Application;
using Application.ArtifactsApi.Schemas;
using Application.Character;
using Application.Jobs.Orchestrators;
using Applicaton.Services.FightSimulator;

namespace Application.Services;

public static class FightEfficiencyCalculator
{
    const float PERCENTAGE_IMPROVEMENT_BOSS_FIGHT_SHOULD_BE = 0.10f;
    public const int SECONDS_PER_TURN = 2;
    public const int ESTIMATED_SECOND_COST_PER_POTION = 60;
    public const int MINIMUM_FIGHT_DURATION = 5;
    public const int REST_HP_PERCENTAGE_PER_SEC = 1;

    public static async ValueTask<bool> IsBossGrindingWorthIt(
        PlayerCharacter character,
        GameState gameState,
        BossGrindDetails bossGrindDetails
    )
    {
        var toughestMonsterOutcome = await GetOutcomeForHighestRelevantMonster(
            character,
            gameState
        );

        if (toughestMonsterOutcome is null)
        {
            return true;
        }

        int toughestMonsterXpEfficiency = GetXpPerHourForMonster(
            character,
            toughestMonsterOutcome.Monster,
            toughestMonsterOutcome
        );

        var bossXpPerHour = bossGrindDetails
            .FightSimResults.Where(simResult => simResult.Schema.Name == character.Name)
            .Select(simResult =>
                GetXpPerHourForMonster(character, bossGrindDetails.Monster, simResult.Outcome)
            )
            .First();

        return IsBossXpGainsSignificantlyBetter(bossXpPerHour, toughestMonsterXpEfficiency);
    }

    static bool IsBossXpGainsSignificantlyBetter(int bossXpPerHour, int comparableMonsterXpPerHour)
    {
        return bossXpPerHour
            >= comparableMonsterXpPerHour * (1 + PERCENTAGE_IMPROVEMENT_BOSS_FIGHT_SHOULD_BE);
    }

    static async Task<FightOutcome?> GetOutcomeForHighestRelevantMonster(
        PlayerCharacter character,
        GameState gameState
    )
    {
        var monsterCandidates = gameState
            .Monsters.Where(monster => monster.Level <= character.Schema.Level)
            .OrderByDescending(monster => monster.Level);

        var bankItems = ItemService.DropSchemaListToItemInInventoryList(
            await gameState.Services.BankItemCache.GetBankItems(character),
            gameState.ItemsDict
        );

        foreach (var monster in monsterCandidates)
        {
            var fightSim = FightSimulator
                .FindBestFightEquipmentIncludingInventory(character, gameState, monster, bankItems)
                .SimResult;

            if (fightSim.Outcome.ShouldFight)
            {
                return fightSim.Outcome;
            }
        }

        return null;
    }

    public static int FightLengthInSeconds(FightOutcome outcome)
    {
        return Math.Min(SECONDS_PER_TURN * outcome.IndividualTurns, 5);
    }

    public static int GetTimeToRest(int maxHp, int hp)
    {
        double percentageHpMissing = (1 - (double)hp / maxHp) * 100;

        int timeToRestSeconds = (int)Math.Ceiling(percentageHpMissing / REST_HP_PERCENTAGE_PER_SEC);

        return timeToRestSeconds;
    }

    public static int TotalSecondsCostForFight(FightOutcome outcome)
    {
        int secondsToFight = FightLengthInSeconds(outcome);

        int secondsPerPotion = ESTIMATED_SECOND_COST_PER_POTION * outcome.PotionsUsed;

        int secondsToRest = outcome.AllPlayerParticipants.Sum(player =>
        {
            int timeToRestSeconds = GetTimeToRest(player.OriginalMaxHp, player.Entity.Hp);

            return timeToRestSeconds;
        });

        return secondsToFight + secondsPerPotion + secondsToRest;
    }

    public static int GetXpPerHourForMonster(
        PlayerCharacter character,
        MonsterSchema monster,
        FightOutcome outcome
    )
    {
        int xpForFight = CalculationService.GetXpForFight(
            character,
            [character.Schema.Level],
            monster
        );

        int fightLength = FightLengthInSeconds(outcome);

        double result = (double)xpForFight / fightLength * 3600;

        return (int)result;
    }
}
