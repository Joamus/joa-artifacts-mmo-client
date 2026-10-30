using Application.Artifacts.Schemas;
using Application.Errors;
using Microsoft.OpenApi.Extensions;
using OneOf.Types;

namespace Application.Services;

public static class SkillService
{
    public static readonly List<Skill> GatheringSkills =
    [
        Skill.Alchemy,
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
        return skill switch
        {
            Skill.Weaponcrafting => "weaponcrafting",
            Skill.Gearcrafting => "gearcrafting",
            Skill.Jewelrycrafting => "jewelrycrafting",
            Skill.Cooking => "cooking",
            Skill.Woodcutting => "woodcutting",
            Skill.Mining => "mining",
            Skill.Alchemy => "alchemy",
            Skill.Fishing => "fishing",
            _ => throw new Exception($"Skill was {skill} - could not find match"),
        };

        // return null;
    }

    public static Skill? GetSkillFromName(string skill)
    {
        return skill switch
        {
            "weaponcrafting" => (Skill?)Skill.Weaponcrafting,
            "gearcrafting" => (Skill?)Skill.Gearcrafting,
            "jewelrycrafting" => (Skill?)Skill.Jewelrycrafting,
            "cooking" => (Skill?)Skill.Cooking,
            "woodcutting" => (Skill?)Skill.Woodcutting,
            "mining" => (Skill?)Skill.Mining,
            "alchemy" => (Skill?)Skill.Alchemy,
            "fishing" => (Skill?)Skill.Fishing,
            _ => null,
        };
    }

    public static SkillKind GetSkillKind(Skill skill)
    {
        // It's a hack, else it won't work correctly
        if (skill == Skill.Alchemy)
        {
            return SkillKind.Crafting;
        }
        return GatheringSkills.Contains(skill) ? SkillKind.Gathering : SkillKind.Crafting;
    }

    public static SkillCrafting SkillToCraftingEffect(Skill skill)
    {
        return skill switch
        {
            Skill.Weaponcrafting => SkillCrafting.WeaponcraftingCrafting,
            Skill.Gearcrafting => SkillCrafting.GearcraftingCrafting,
            Skill.Jewelrycrafting => SkillCrafting.JewelrycraftingCrafting,
            Skill.Cooking => SkillCrafting.CookingCrafting,
            Skill.Woodcutting => SkillCrafting.WoodcuttingCrafting,
            Skill.Mining => SkillCrafting.MiningCrafting,
            Skill.Alchemy => SkillCrafting.AlchemyCrafting,
            Skill.Fishing => SkillCrafting.FishingCrafting,
            _ => throw new AppError($"Could not find crafting skill for {skill.GetDisplayName()}"),
        };
    }

    public static SkillCrafting? CraftingEffectToSkill(string effect)
    {
        return effect switch
        {
            "weaponcrafting_crafting" => SkillCrafting.WeaponcraftingCrafting,
            "gearcrafting_crafting" => SkillCrafting.GearcraftingCrafting,
            "jewelrycrafting_crafting" => SkillCrafting.JewelrycraftingCrafting,
            "cooking_crafting" => SkillCrafting.CookingCrafting,
            "woodcutting_crafting" => SkillCrafting.WoodcuttingCrafting,
            "mining_crafting" => SkillCrafting.MiningCrafting,
            "alchemy_crafting" => SkillCrafting.AlchemyCrafting,
            "fishing_crafting" => SkillCrafting.FishingCrafting,
            _ => null,
        };
    }

    public static SkillGathering SkillToGatheringEffect(Skill skill)
    {
        return skill switch
        {
            Skill.Weaponcrafting => SkillGathering.WeaponcraftingGathering,
            Skill.Gearcrafting => SkillGathering.GearcraftingGathering,
            Skill.Jewelrycrafting => SkillGathering.JewelrycraftingGathering,
            Skill.Cooking => SkillGathering.CookingGathering,
            Skill.Woodcutting => SkillGathering.WoodcuttingGathering,
            Skill.Mining => SkillGathering.MiningGathering,
            Skill.Alchemy => SkillGathering.AlchemyGathering,
            Skill.Fishing => SkillGathering.FishingGathering,
            _ => throw new AppError($"Could not find gathering skill for {skill.GetDisplayName()}"),
        };
    }

    public static SkillGathering? GatheringEffectToSkill(string effect)
    {
        return effect switch
        {
            "weaponcrafting_gathering" => SkillGathering.WeaponcraftingGathering,
            "gearcrafting_gathering" => SkillGathering.GearcraftingGathering,
            "jewelrycrafting_gathering" => SkillGathering.JewelrycraftingGathering,
            "cooking_gathering" => SkillGathering.CookingGathering,
            "woodcutting_gathering" => SkillGathering.WoodcuttingGathering,
            "mining_gathering" => SkillGathering.MiningGathering,
            "alchemy_gathering" => SkillGathering.AlchemyGathering,
            "fishing_gathering" => SkillGathering.FishingGathering,
            _ => null,
        };
    }
}

public enum SkillKind
{
    Crafting,
    Gathering,
}
