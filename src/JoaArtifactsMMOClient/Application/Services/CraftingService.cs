using Application.Artifacts.Schemas;
using Application.ArtifactsApi.Schemas;
using Application.Character;

namespace Application.Services;

public static class CraftingService
{
    static (int XpBase, int Coefficient) GetBaseXpItem(int itemLevel)
    {
        if (itemLevel < 5)
        {
            return (50, 25);
        }
        else if (itemLevel < 10)
        {
            return (100, 30);
        }
        else if (itemLevel < 15)
        {
            return (200, 35);
        }
        else if (itemLevel < 20)
        {
            return (325, 40);
        }
        else if (itemLevel < 25)
        {
            return (450, 45);
        }
        else if (itemLevel < 30)
        {
            return (550, 50);
        }
        else if (itemLevel < 35)
        {
            return (650, 55);
        }
        else if (itemLevel < 40)
        {
            return (750, 60);
        }
        else if (itemLevel < 45)
        {
            return (850, 65);
        }
        else
        {
            return (1000, 70);
        }
    }

    static float GetSkillMultiplier(Skill skill)
    {
        return skill switch
        {
            Skill.Mining | Skill.Woodcutting | Skill.Fishing => 0.1f,
            Skill.Cooking => 0.5f,
            _ => 1.0f,
        };
    }

    public static int GetXpForCraftingItem(int skillLevel, ItemSchema item, int wisdom = 0)
    {
        if (item.Craft is null)
        {
            return 0;
        }

        return InnerGetXpForCraftingItem(skillLevel, item.Level, item.Craft.Skill, wisdom);
    }

    public static int InnerGetXpForCraftingItem(
        int skillLevel,
        int itemLevel,
        Skill skill,
        int wisdom = 0
    )
    {
        if (skillLevel > itemLevel + PlayerActionService.LEVEL_DIFF_NO_XP)
        {
            return 0;
        }

        (int xpBase, int xpCoefficient) = GetBaseXpItem(itemLevel);

        float skillMultiplier = GetSkillMultiplier(skill);

        float wisdomBonus = 1 + (wisdom / 100);

        int result = (int)
            Math.Round(
                (xpBase + (itemLevel / skillLevel) * xpCoefficient) * skillMultiplier * wisdomBonus
            );

        return result;
    }
}
