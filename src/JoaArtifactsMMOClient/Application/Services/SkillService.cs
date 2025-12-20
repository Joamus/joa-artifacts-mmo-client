using Application.Artifacts.Schemas;

namespace Application.Services;

public static class SkillService
{
    public static readonly List<Skill> GatheringSkills =
    [
        // Skill.Alchemy,
        Skill.Fishing,
        Skill.Mining,
        Skill.Woodcutting,
    ];

    // public static readonly string[] GatheringSkills = ["fishing", "mining", "woodcutting"];
    public static readonly List<Skill> CraftingSkills =
    [
        Skill.Weaponcrafting,
        Skill.Gearcrafting,
        Skill.Jewelrycrafting,
        Skill.Cooking,
        Skill.Alchemy,
    ];

    public static string GetSkillName(Skill skill)
    {
        switch (skill)
        {
            case Skill.Weaponcrafting:
                return "weaponcrafting";
            case Skill.Gearcrafting:
                return "gearcrafting";
            case Skill.Jewelrycrafting:
                return "jewelrycrafting";
            case Skill.Cooking:
                return "cooking";
            case Skill.Woodcutting:
                return "woodcutting";
            case Skill.Mining:
                return "mining";
            case Skill.Alchemy:
                return "alchemy";
            case Skill.Fishing:
                return "fishing";
        }

        throw new Exception($"Skill was {skill} - could not find match");

        // return null;
    }

    public static Skill? GetSkillFromName(string skill)
    {
        switch (skill)
        {
            case "weaponcrafting":
                return Skill.Weaponcrafting;
            case "gearcrafting":
                return Skill.Gearcrafting;
            case "jewelrycrafting":
                return Skill.Jewelrycrafting;
            case "cooking":
                return Skill.Cooking;
            case "woodcutting":
                return Skill.Woodcutting;
            case "mining":
                return Skill.Mining;
            case "alchemy":
                return Skill.Alchemy;
            case "fishing":
                return Skill.Fishing;
        }

        return null;
    }
}

public enum SkillKind
{
    Crafting,
    Gathering,
}
