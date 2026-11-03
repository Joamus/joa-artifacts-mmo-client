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

    static float GetCraftingMultiplier(Skill skill, ItemSchema item)
    {
        switch (skill)
        {
            case Skill.Weaponcrafting:
            case Skill.Gearcrafting:
            case Skill.Jewelrycrafting:
            case Skill.Cooking:
            case Skill.Alchemy:
                int ingredientCount = GetMaterialCount(item);

                double initialResult = 0.2832 + 0.2171 * Math.Sqrt(Math.Max(1, ingredientCount));

                return (float)Math.Clamp(initialResult, 0.50, 1.55);
            default:
                return 0.1f;
        }
        ;
    }

    static int GetMaterialCount(ItemSchema item)
    {
        return item.Craft?.Items.Sum(item => item.Quantity) ?? 0;
    }

    public static int GetXpForCraftingItem(int skillLevel, ItemSchema item, int wisdom = 0)
    {
        if (item.Craft is null)
        {
            return 0;
        }

        return InnerGetXpForCraftingItem(skillLevel, item, item.Craft.Skill, wisdom);
    }

    static int InnerGetXpForCraftingItem(
        int skillLevel,
        ItemSchema item,
        Skill skill,
        int wisdom = 0
    )
    {
        if (skillLevel > item.Level + PlayerActionService.LEVEL_DIFF_NO_XP)
        {
            return 0;
        }

        (int xpBase, int xpCoefficient) = GetBaseXpItem(item.Level);

        // float skillMultiplier = GetSkillMultiplier(skill);
        float craftingMultiplier = GetCraftingMultiplier(skill, item);

        float wisdomBonus = 1 + (wisdom / 100);

        int result = (int)
            Math.Round(
                // (xpBase + (item.Level / skillLevel) * xpCoefficient) * skillMultiplier * wisdomBonus
                (xpBase + (item.Level / skillLevel) * xpCoefficient)
                    * craftingMultiplier
                    * wisdomBonus
            );

        return result;
    }
}
