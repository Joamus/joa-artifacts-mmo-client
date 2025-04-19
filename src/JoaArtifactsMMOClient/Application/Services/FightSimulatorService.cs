using Application.ArtifactsApi.Schemas;

namespace Applicaton.Services.FightSimulator;

public static class FightSimulatorService
{
    private static readonly double CRIT_DAMAGE_MODIFIER = 0.5;

    public static FightOutcome CalculateFightOutcome(
        CharacterSchema character,
        MonsterSchema monster,
        bool playerFullHp = true
    )
    {
        var remainingPlayerHp = playerFullHp ? character.MaxHp : character.Hp;
        var remainingMonsterHp = monster.Hp;

        FightResult? outcome = null;

        int turns = 0;

        while (outcome is null)
        {
            turns++;
            if (remainingPlayerHp <= 0)
            {
                outcome = FightResult.Loss;
                break;
            }
            else if (remainingMonsterHp <= 0)
            {
                outcome = FightResult.Win;
                break;
            }

            int playerDamage = CalculatePlayerDamage(character, monster);

            remainingMonsterHp -= playerDamage;

            if (remainingMonsterHp <= 0)
            {
                outcome = FightResult.Win;
                break;
            }

            int monsterDamage = CalculateMonsterDamage(monster, character);

            remainingPlayerHp -= monsterDamage;

            if (remainingPlayerHp <= 0)
            {
                outcome = FightResult.Loss;
                break;
            }
        }

        // TODO: Implement
        return new FightOutcome
        {
            Result = outcome ?? FightResult.Loss, // Should not be necessary
            PlayerHp = remainingPlayerHp,
            MonsterHp = remainingMonsterHp,
            TotalTurns = turns,
            ShouldFight =
                outcome == FightResult.Win && remainingPlayerHp >= (character.MaxHp * 0.35),
        };
    }

    private static int CalculateMonsterDamage(MonsterSchema monster, CharacterSchema character)
    {
        int fireDamage =
            CalculateElementalAttack(monster.AttackFire, 0, 0, monster.CriticalStrike)
            / (1 + character.ResFire);
        int earthDamage =
            CalculateElementalAttack(monster.AttackEarth, 0, 0, monster.CriticalStrike)
            / (1 + character.ResEarth);
        int waterDamage =
            CalculateElementalAttack(monster.AttackWater, 0, 0, monster.CriticalStrike)
            / (1 + character.ResWater);
        int airDamage =
            CalculateElementalAttack(monster.AttackAir, 0, 0, monster.CriticalStrike)
            / (1 + character.ResAir);

        return fireDamage + earthDamage + waterDamage + airDamage;
    }

    private static int CalculatePlayerDamage(CharacterSchema character, MonsterSchema monster)
    {
        int fireDamage =
            CalculateElementalAttack(
                character.AttackFire,
                character.DmgFire,
                character.Dmg,
                character.CriticalStrike
            ) / (1 + monster.ResFire);
        int earthDamage =
            CalculateElementalAttack(
                character.AttackEarth,
                character.DmgEarth,
                character.Dmg,
                character.CriticalStrike
            ) / (1 + monster.ResEarth);
        int waterDamage =
            CalculateElementalAttack(
                character.AttackWater,
                character.DmgWater,
                character.Dmg,
                character.CriticalStrike
            ) / (1 + monster.ResWater);
        int airDamage =
            CalculateElementalAttack(
                character.AttackAir,
                character.DmgAir,
                character.Dmg,
                character.CriticalStrike
            ) / (1 + monster.ResAir);

        return fireDamage + earthDamage + waterDamage + airDamage;
    }

    private static int CalculateElementalAttack(
        int baseDamage,
        int elementalMultiplier,
        int damageMultiplier,
        int critChance
    )
    {
        int damage = (int)
            Math.Round(baseDamage + baseDamage * (elementalMultiplier + damageMultiplier) * 0.01); // Not sure where the 0.01 is from

        return (int)(damage * 1 + (critChance * 0.01 * (1 + CRIT_DAMAGE_MODIFIER)));
    }
}

public record FightOutcome
{
    public FightResult Result { get; init; }

    public int PlayerHp { get; init; }

    public int MonsterHp { get; init; }

    public int TotalTurns { get; init; }

    public bool ShouldFight { get; init; }
}
