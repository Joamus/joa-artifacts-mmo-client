using Application.Artifacts.Schemas;
using Application.ArtifactsApi.Schemas;
using Application.ArtifactsApi.Schemas.Responses;
using Application.Character;
using Application.Errors;
using Application.Services;
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
        "jewelrycrafting",
        "cooking",
        "alchemy",
    ];

    // public static readonly string[] JobTypesToAvoidWhenCrafting = ["FightMonster", "CompleteTask"];
    public static readonly string[] JobTypesToAvoidWhenCrafting = [];
    public Skill Skill { get; init; }

    private string skillName { get; set; }
    public int LevelOffset { get; private set; }

    public bool Relative { get; init; }

    public int SkillLevel { get; set; }

    bool firstRun { get; set; } = true;

    public TrainSkill(
        PlayerCharacter character,
        GameState gameState,
        Skill skill,
        int level,
        bool relative = false
    )
        : base(character, gameState)
    {
        Skill = skill;
        LevelOffset = level;
        Relative = relative;
        skillName = SkillService.GetSkillName(Skill);
    }

    protected override async Task<OneOf<AppError, None>> ExecuteAsync()
    {
        // Only runs the first time this job runs, if it's a relative level job. If it queues a job before itself, it shouldn't recalculate the level
        if (firstRun)
        {
            SkillLevel = Character.GetSkillLevel(skillName);
        }

        int untilLevel;

        if (Relative)
        {
            untilLevel = SkillLevel + LevelOffset;

            if (!firstRun && untilLevel <= Character.GetSkillLevel(skillName))
            {
                return new None();
            }
        }
        else
        {
            untilLevel = LevelOffset;
        }

        logger.LogInformation(
            $"{JobName}: [{Character.Schema.Name}] run started - training {skillName} until level {untilLevel}"
        );

        SkillKind skillKind = GatheringSkills.Contains(skillName)
            ? SkillKind.Gathering
            : SkillKind.Crafting;

        if (SkillLevel < untilLevel)
        {
            var jobs = await GetJobsRequired(Skill, skillKind, SkillLevel);

            if (jobs.Count > 0)
            {
                firstRun = false;
                Character.QueueJobsBefore(Id, jobs);
            }
        }

        return new None();
    }

    public async Task<List<CharacterJob>> GetJobsRequired(
        Skill skill,
        SkillKind skillKind,
        int skillLevel
    )
    {
        // We want to find the the thing that we can gather/craft that is closest to us in skill

        List<CharacterJob> trainJobs = [];

        switch (skillKind)
        {
            case SkillKind.Gathering:

                ResourceSchema? bestResource = null;

                foreach (var resource in gameState.Resources)
                {
                    if (resource.Skill == skill && resource.Level <= skillLevel)
                    {
                        if (bestResource is null || bestResource.Level < resource.Level)
                        {
                            bestResource = resource;
                        }
                    }
                }

                if (bestResource is null)
                {
                    throw new AppError(
                        $"Could not find best resource for training \"{skill}\" at skill level \"{skillLevel}\""
                    );
                }

                var itemToGather = bestResource.Drops.Find(drop =>
                    gameState.ItemsDict.GetValueOrNull(drop.Code)?.Level <= skillLevel
                );

                var gatherJob = new GatherResourceItem(
                    Character,
                    gameState,
                    itemToGather!.Code,
                    AMOUNT_TO_GATHER_PER_JOB,
                    false
                );

                // We don't want to keep the items in our inventory forever - just craft them and deposit.
                gatherJob.ForBank();

                trainJobs.Add(gatherJob);
                break;
            case SkillKind.Crafting:
                // We have to consider the amount of materials needed, and prioritize not having materials that require task items, etc.

                ItemSchema? bestItemToCraft = null;

                List<ItemSchema> itemToCraftCandidates = [];

                foreach (var item in gameState.Items)
                {
                    if (
                        item.Craft is not null
                        && item.Craft.Skill == skill
                        && item.Level <= skillLevel
                        && (skillLevel - item.Craft.Level) < LEVEL_DIFF_FOR_NO_XP
                    // && ()
                    )
                    {
                        if (bestItemToCraft is null || bestItemToCraft.Level < item.Level)
                        {
                            itemToCraftCandidates.Add(item);
                        }
                    }
                }

                var bankItemsResponse = await gameState.BankItemCache.GetBankItems(Character);

                if (bankItemsResponse is null)
                {
                    throw new AppError("Failed to get bank items");
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
                            return -1;
                        }

                        int resultACost = resultA.Item2 + skillLevel - a.Craft!.Level;

                        var resultB = GetInconvenienceCostCraftItem(
                            b,
                            gameState,
                            bankItemsResponse.Data,
                            Character
                        );

                        int resultBCost = resultB.Item2 + skillLevel - b.Craft!.Level;

                        if (!resultB.Item1)
                        {
                            return 1;
                        }

                        return resultACost.CompareTo(resultBCost);
                    }
                );

                bestItemToCraft =
                    // itemToCraftCandidates.FirstOrDefault(candidate =>
                    //     skillLevel - candidate.Craft!.Level < 5 // there are basically always new items to craft every 5 levels
                    // ) ?? itemToCraftCandidates.ElementAtOrDefault(0);
                    itemToCraftCandidates.ElementAtOrDefault(0);

                if (bestItemToCraft is null)
                {
                    if (Skill == Skill.Alchemy)
                    {
                        // TODO: Hardcoded - you can't craft anything in Alchemy before being level 5, so you need to gather sunflowers until then.

                        var job = new GatherResourceItem(
                            Character,
                            gameState,
                            "sunflower",
                            AMOUNT_TO_GATHER_PER_JOB,
                            false
                        );
                        job.ForBank();
                        trainJobs.Add(job);
                        return trainJobs;
                    }

                    throw new AppError(
                        $"Could not find best item for training \"{skill}\" at skill level \"{skillLevel}\" for \"{Character.Schema.Name}\""
                    );
                }

                // Weapon, gear, and jewellery crafting often requires a lot of raw materials, which fill up the inventory faster.
                // Cooking and alchemy rarely do, so we can cook/make potions in bigger batches.
                int craftingAmount = 1;

                if (Skill == Skill.Alchemy || Skill == Skill.Cooking)
                {
                    craftingAmount = 20;
                }

                logger.LogInformation(
                    $"{JobName}: [{Character.Schema.Name}] will be crafting {craftingAmount} x {bestItemToCraft.Code} to train {skill} until level {skillLevel}"
                );

                var obtainItemJob = new ObtainItem(
                    Character,
                    gameState,
                    bestItemToCraft.Code,
                    craftingAmount
                );

                // obtainItemJob.AllowFindingItemInBank = false;
                obtainItemJob.AllowUsingMaterialsFromBank = true;
                obtainItemJob.AllowUsingMaterialsFromInventory = true;

                trainJobs.Add(obtainItemJob);

                if (RecycleItem.CanItemBeRecycled(bestItemToCraft))
                {
                    obtainItemJob.onSuccessEndHook = () =>
                    {
                        var recycleJob = new RecycleItem(
                            Character,
                            gameState,
                            bestItemToCraft.Code,
                            craftingAmount
                        ).SetParent<RecycleItem>(obtainItemJob);

                        recycleJob.ForBank();

                        return Task.Run(() => { });
                    };
                }
                else
                {
                    obtainItemJob.ForBank();
                }
                break;
        }

        if (trainJobs.Count > 0)
        {
            return trainJobs;
        }
        else
        {
            throw new AppError($"Could not find a way to train skill \"{skill}\" to {skillLevel}");
        }
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
                        var fightOutcome = FightSimulator
                            .FindBestFightEquipment(Character, gameState, monster)
                            .Outcome;

                        if (
                            fightOutcome.ShouldFight
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
                    else
                    {
                        canObtain = false;
                    }
                }
            }
        }

        return (canObtain, score);
    }
}
