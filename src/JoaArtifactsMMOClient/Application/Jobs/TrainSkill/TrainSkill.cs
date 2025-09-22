using System.Collections;
using Application.Artifacts.Schemas;
using Application.ArtifactsApi.Schemas;
using Application.ArtifactsApi.Schemas.Responses;
using Application.Character;
using Application.Errors;
using Applicaton.Services.FightSimulator;
using Microsoft.Extensions.ObjectPool;
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
        "jewelrycrafting",
        "cooking",
        "alchemy",
    ];

    // public static readonly string[] JobTypesToAvoidWhenCrafting = ["FightMonster", "CompleteTask"];
    public static readonly string[] JobTypesToAvoidWhenCrafting = [];
    public Skill Skill { get; init; }

    private string skillName { get; set; }
    public int UntilLevel { get; init; }

    private static ILogger<TrainSkill> staticLogger = LoggerFactory
        .Create(AppLogger.options)
        .CreateLogger<TrainSkill>();

    public TrainSkill(PlayerCharacter character, GameState gameState, Skill skill, int untilLevel)
        : base(character, gameState)
    {
        Skill = skill;
        UntilLevel = untilLevel;
        skillName = GetSkillName(Skill);
    }

    protected override async Task<OneOf<AppError, None>> ExecuteAsync()
    {
        logger.LogInformation(
            $"{GetType().Name}: [{Character.Schema.Name}] run started - training {skillName} until level {UntilLevel}"
        );
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
                    Status = JobStatus.Suspend;
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

                var gatherJob = new GatherResourceItem(
                    Character,
                    gameState,
                    bestResource.Code,
                    AMOUNT_TO_GATHER_PER_JOB,
                    false
                );

                // We don't want to keep the items in our inventory forever - just craft them and deposit.
                gatherJob.ForBank();

                return gatherJob;
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
                        && (skillLevel - item.Craft.Level) < LEVEL_DIFF_FOR_NO_XP
                    // && ()
                    )
                    {
                        if (bestItemToCraft is null || bestItemToCraft.Level < item.Level)
                        {
                            // Jobs is mutated in the method
                            List<CharacterJob> jobs = [];

                            // await ObtainItem.GetJobsRequired(
                            //     Character,
                            //     gameState,
                            //     true,
                            //     [],
                            //     jobs,
                            //     item.Code,
                            //     1
                            // );

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

                var bankItemsResponse = await gameState.AccountRequester.GetBankItems();

                if (bankItemsResponse is null)
                {
                    return new AppError("Failed to get bank items");
                }

                // The difference in skill level is essentially a cost, because we get less XP.
                itemToCraftCandidates.Sort(
                    (a, b) =>
                    {
                        var resultA = (
                            GetInconvenienceCostCraftItem(
                                a,
                                gameState,
                                bankItemsResponse.Data,
                                Character
                            )
                        // + skillLevel
                        // - a.Craft!.Level
                        );

                        if (!resultA.Item1)
                        {
                            return 1;
                        }

                        int resultAScore = resultA.Item2 + skillLevel - a.Craft!.Level;

                        var resultB = GetInconvenienceCostCraftItem(
                            b,
                            gameState,
                            bankItemsResponse.Data,
                            Character
                        );

                        int resultBScore = resultB.Item2 + skillLevel - b.Craft!.Level;

                        if (!resultB.Item1)
                        {
                            return 0;
                        }

                        return resultAScore.CompareTo(resultBScore);
                    }
                );

                bestItemToCraft =
                    itemToCraftCandidates.FirstOrDefault(candidate =>
                        skillLevel - candidate.Craft!.Level < 5 // there are basically always new items to craft every 5 levels
                    ) ?? itemToCraftCandidates.ElementAtOrDefault(0);

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

                // Weapon, gear, and jewel crafting often requires a lot of raw materials, which fill up the inventory faster.
                // Cooking and alchemy rarely do, so we can cook/make potions in bigger batches.
                int craftingAmount = 1;

                if (Skill == Skill.Alchemy || Skill == Skill.Cooking)
                {
                    craftingAmount = 20;
                }

                logger.LogInformation(
                    $"{GetType().Name}: [{Character.Schema.Name}] will be crafting {craftingAmount} x {bestItemToCraft.Code} to train {skillName} until level {UntilLevel}"
                );

                var obtainItemJob = new ObtainItem(
                    Character,
                    gameState,
                    bestItemToCraft.Code,
                    craftingAmount
                );

                obtainItemJob.AllowFindingItemInBank = false;
                obtainItemJob.AllowUsingMaterialsFromBank = true;
                obtainItemJob.AllowUsingMaterialsFromInventory = true;
                // obtainItemJob.CanTriggerTraining = true;
                // We don't want to keep the items in our inventory forever - just craft them and deposit.
                obtainItemJob.ForBank();

                return obtainItemJob;
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

    public static (bool, int) GetInconvenienceCostCraftItem(
        ItemSchema item,
        GameState gameState,
        List<DropSchema> bankItems,
        PlayerCharacter Character
    )
    {
        int score = 0;
        bool canObtain = true;

        if (item.Craft?.Items is not null)
        {
            foreach (var _item in item.Craft.Items)
            {
                bool matchInBank =
                    bankItems.Find(bankItem =>
                        bankItem.Code == _item.Code && bankItem.Quantity >= _item.Quantity
                    )
                        is not null;

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

                    MonsterSchema? monsterWeCanFight = null;
                    FightOutcome? monsterWeCanFightOutcome = null;

                    foreach (var monster in monstersThatDropTheItem)
                    {
                        var fightOutcome = FightSimulator.CalculateFightOutcomeWithBestEquipment(
                            Character,
                            monster,
                            gameState
                        );

                        if (
                            fightOutcome!.ShouldFight
                            && (
                                monsterWeCanFightOutcome?.TotalTurns is null
                                || monsterWeCanFightOutcome.TotalTurns > fightOutcome.TotalTurns
                            )
                        )
                        {
                            monsterWeCanFight = monster;
                            monsterWeCanFightOutcome = fightOutcome;
                            break;
                        }
                    }

                    if (monsterWeCanFight is not null)
                    {
                        score +=
                            1 + (int)Math.Round((float)monsterWeCanFightOutcome!.TotalTurns / 10);
                    }
                }
            }
        }

        return (canObtain, score);
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

        throw new Exception($"Could not find skill - input: \"{skill}\"");
    }

    public static Skill GetSkillFromName(string skill)
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

        throw new Exception($"Could not find skill - input: \"{skill}\"");
    }

    public enum SkillKind
    {
        Crafting,
        Gathering,
    }
}
