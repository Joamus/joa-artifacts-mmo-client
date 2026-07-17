using System.Collections;
using Application.ArtifactsApi.Schemas;
using Application.Character;
using Application.Errors;
using Application.Records;

namespace Application.Services;

public static class CalculationService
{
    public static int CalculateDistanceToMap(int originX, int originY, int mapX, int mapY)
    {
        int xDiff = Math.Max(mapX, originX) - Math.Min(mapX, originX);
        int yDiff = Math.Max(mapY, originY) - Math.Min(mapY, originY);
        return Math.Abs(xDiff + yDiff);
    }

    public static void SortItemsBasedOnEffect(
        List<ItemSchema> items,
        string effectName,
        bool ascending = false
    )
    {
        items.Sort(
            (a, b) =>
            {
                var aHealValue = a.Effects.Find(effect => effect.Code == effectName)?.Value ?? 0;

                var bHealValue = b.Effects.Find(effect => effect.Code == effectName)?.Value ?? 0;

                if (ascending)
                {
                    return aHealValue.CompareTo(bHealValue);
                }
                else
                {
                    return bHealValue.CompareTo(aHealValue);
                }
            }
        );
    }

    public static void SortItemsBasedOnEffect(
        List<ItemInInventory> items,
        string effectName,
        bool ascending = false
    )
    {
        items.Sort(
            (a, b) =>
            {
                var aHealValue =
                    a.Item.Effects.Find(effect => effect.Code == effectName)?.Value ?? 0;

                var bHealValue =
                    b.Item.Effects.Find(effect => effect.Code == effectName)?.Value ?? 0;

                if (ascending)
                {
                    return aHealValue.CompareTo(bHealValue);
                }
                else
                {
                    return bHealValue.CompareTo(aHealValue);
                }
            }
        );
    }

    public static int GetXpForFight(
        CharacterSchema character,
        List<int> allFightingCharacterLevels,
        MonsterSchema monster
    )
    {
        double averagePlayerLevel = Math.Floor(
            (float)allFightingCharacterLevels.Sum(level => level) / allFightingCharacterLevels.Count
        );

        float wisdomBonus = 1 + (character.Wisdom / 100);

        if (averagePlayerLevel + PlayerActionService.LEVEL_DIFF_NO_XP > monster.Level)
        {
            return 0;
        }

        float levelPenalty = 0;

        if (averagePlayerLevel <= monster.Level)
        {
            levelPenalty = 1.0f;
        }
        else if (averagePlayerLevel >= monster.Level + 5)
        {
            levelPenalty = 0.7f;
        }

        float monsterMultiplier = monster.Type switch
        {
            MonsterType.Normal => 1.0f,
            MonsterType.Elite => 1.4f,
            MonsterType.Boss => 2.0f,
            MonsterType.RaidBoss => 2.0f,
            _ => throw new AppError($"Invalid monster type {monster.Type}"),
        };

        int xp = (int)
            Math.Round(
                ((monster.Level / averagePlayerLevel) * 20 + monster.Hp * 0.04)
                    * levelPenalty
                    * monsterMultiplier
                    * wisdomBonus
            );

        return xp;
    }
}
