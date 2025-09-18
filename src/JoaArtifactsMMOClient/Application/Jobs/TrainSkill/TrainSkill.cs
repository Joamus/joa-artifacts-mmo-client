using System.Collections;
using Application.Artifacts.Schemas;
using Application.ArtifactsApi.Schemas;
using Application.ArtifactsApi.Schemas.Responses;
using Application.Character;
using Application.Errors;
using Applicaton.Services.FightSimulator;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public class TrainSkill : CharacterJob
{
    public static int AMOUNT_TO_GATHER_PER_JOB = 20;

    public static int LEVEL_DIFF_FOR_NO_XP = 10;

    public static readonly string[] GatheringSkills = ["fishing", "mining", "woodcutting"];
    public static readonly string[] CraftingSkills =
    [
        "weaponcrafting",
        "gearcrafting",
        "jewelcrafting",
        "cooking",
        "alchemy",
    ];

    // public static readonly string[] JobTypesToAvoidWhenCrafting = ["FightMonster", "CompleteTask"];
    public static readonly string[] JobTypesToAvoidWhenCrafting = [];
    public Skill Skill { get; init; }

    private string skillName { get; set; }
    public int UntilLevel { get; init; }

    public TrainSkill(PlayerCharacter character, GameState gameState, Skill skill, int untilLevel)
        : base(character, gameState)
    {
        Skill = skill;
        UntilLevel = untilLevel;
        skillName = GetSkillName(Skill);
    }

    protected override async Task<OneOf<AppError, None>> ExecuteAsync()
    {
        int skillLevel = GetSkillLevel(skillName);

        SkillKind skillKind = GatheringSkills.Contains(skillName)
            ? SkillKind.Gathering
            : SkillKind.Crafting;

        if (skillLevel < UntilLevel)
        {
            var result = await GetJobRequired(skillName, skillKind, skillLevel);

            switch (result.Value)
            {
                case CharacterJob job:
                    Character.QueueJobsBefore(Id, [job]);
                    break;
                case AppError error:
                    return error;
            }
        }

        return new None();
    }

    public async Task<OneOf<AppError, CharacterJob>> GetJobRequired(
        string skillName,
        SkillKind skillKind,
        int skillLevel
    )
    {
        if (Skill == Skill.Cooking)
        {
            // Take a shortcut and just queue ObtainSuitableFood
            // They will also level fishing a bit. It's the easiest way to level cooking
            return new ObtainSuitableFood(
                Character,
                gameState,
                PlayerCharacter.AMOUNT_OF_FOOD_TO_KEEP
            );
        }

        // We want to find the the thing that we can gather/craft that is closest to us in skill

        switch (skillKind)
        {
            case SkillKind.Gathering:

                ResourceSchema? bestResource = null;

                foreach (var resource in gameState.Resources)
                {
                    if (resource.Skill == skillName && resource.Level <= skillLevel)
                    {
                        if (bestResource is null || bestResource.Level < resource.Level)
                        {
                            bestResource = resource;
                        }
                    }
                }

                if (bestResource is null)
                {
                    return new AppError(
                        $"Could not find best resource for training \"{skillName}\" at skill level \"{skillLevel}\""
                    );
                }

                return new GatherResourceItem(
                    Character,
                    gameState,
                    bestResource.Code,
                    AMOUNT_TO_GATHER_PER_JOB,
                    false
                );
            case SkillKind.Crafting:
                // We have to consider the amount of materials needed, and prioritize not having materials that require task items, etc.

                ItemSchema? bestItemToCraft = null;

                List<ItemSchema> itemToCraftCandidates = [];

                foreach (var item in gameState.Items)
                {
                    if (
                        item.Craft is not null
                        && GetSkillName(item.Craft.Skill) == skillName
                        && item.Level <= skillLevel
                        && (skillLevel - item.Level) < LEVEL_DIFF_FOR_NO_XP
                    )
                    {
                        if (bestItemToCraft is null || bestItemToCraft.Level < item.Level)
                        {
                            // Jobs is mutated in the method
                            List<CharacterJob> jobs = [];

                            await ObtainItem.GetJobsRequired(
                                Character,
                                gameState,
                                true,
                                [],
                                jobs,
                                item.Code,
                                1
                            );

                            // Dumb implementation - we only want jobs where we can craft everything
                            if (
                                jobs.Find(job =>
                                    JobTypesToAvoidWhenCrafting.Contains(job.GetType().Name)
                                )
                                is null
                            )
                            {
                                // bestItemToCraft = item;
                                itemToCraftCandidates.Add(item);
                            }
                        }
                    }
                }

                itemToCraftCandidates.Sort(
                    (a, b) =>
                    {
                        int aScore = 0;

                        bool aNeedsTaskMaterials =
                            a.Craft?.Items.Find(item =>
                                gameState.ItemsDict.GetValueOrNull(item.Code)?.Subtype == "task"
                            )
                                is not null;

                        aScore += aNeedsTaskMaterials ? 1 : 0;

                        bool aNeedsMonsterDropMaterials =
                            a.Craft?.Items.Find(item =>
                                gameState.ItemsDict.GetValueOrNull(item.Code)?.Subtype == "mob"
                            )
                                is not null;

                        aScore += aNeedsMonsterDropMaterials ? 1 : 0;

                        bool bNeedsTaskMaterials =
                            b.Craft?.Items.Find(item =>
                                gameState.ItemsDict.GetValueOrNull(item.Code)?.Subtype == "task"
                            )
                                is not null;

                        return aNeedsTaskMaterials.CompareTo(bNeedsTaskMaterials);
                    }
                );

                // foreach (var candiate in itemToCraftCandidates) { }

                if (bestItemToCraft is null)
                {
                    if (Skill == Skill.Alchemy)
                    {
                        // TODO: Hardcoded - you can't craft anything in Alchemy before being level 5, so you need to gather sunflowers until then.
                        return new GatherResourceItem(
                            Character,
                            gameState,
                            "sunflower",
                            AMOUNT_TO_GATHER_PER_JOB,
                            false
                        );
                    }

                    return new AppError(
                        $"Could not find best item for training \"{skillName}\" at skill level \"{UntilLevel}\" for \"{Character.Schema.Name}\""
                    );
                }

                return new ObtainItem(
                    Character,
                    gameState,
                    bestItemToCraft.Code,
                    1 // Craft 1 at a time, can take a lot of mats
                );
        }

        return new AppError($"Could not find a way to train skill \"{skillName}\" to {skillLevel}");
    }

    int GetSkillLevel(string skill)
    {
        var prop = Character
            .Schema.GetType()
            .GetProperty((skill + "_level").FromSnakeToPascalCase());

        var value = (int)prop!.GetValue(Character.Schema)!;

        return value;
    }

    public static int GetInconvenienceCostCraftItem(
        ItemSchema item,
        GameState gameState,
        List<DropSchema> bankItems,
        PlayerCharacter Character
    )
    {
        int score = 0;

        if (item.Craft?.Items is not null)
        {
            foreach (var _item in item.Craft.Items)
            {
                bool matchInBank =
                    bankItems.Find(bankItem =>
                        bankItem.Code == _item.Code && bankItem.Quantity >= _item.Quantity
                    )
                        is null;

                if (matchInBank)
                {
                    continue;
                }

                var matchingItem = gameState.ItemsDict.GetValueOrNull(_item.Code);

                if (matchingItem?.Subtype == "task")
                {
                    score += 2;
                }
                else if (matchingItem?.Subtype == "mob")
                {
                    var monstersThatDropTheItem = gameState.Monsters.FindAll(monster =>
                        monster.Drops.Find(drop => drop.Code == _item.Code) is not null
                    );

                    foreach (var monster in monstersThatDropTheItem)
                    {
                        var fightOutcome = FightSimulator.CalculateFightOutcome(
                            Character.Schema,
                            monster
                        );

                        if (fightOutcome.ShouldFight)
                        {
                            score += 1;
                        }
                    }

                    score += 100;
                }
            }
        }

        return score;
    }

    public static string GetSkillName(Skill skill)
    {
        switch (skill)
        {
            case Skill.Weaponcrafting:
                return "weaponcrafting";
            case Skill.Gearcrafting:
                return "gearcrafting";
            case Skill.Jewelrycrafting:
                return "jewelcrafting";
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

        throw new Exception($"Could not find skill - input: \"{skill}\"");
    }

    public enum SkillKind
    {
        Crafting,
        Gathering,
    }
}
