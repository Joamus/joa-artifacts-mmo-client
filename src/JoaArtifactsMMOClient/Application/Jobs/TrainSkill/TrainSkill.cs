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
        if (Relative && firstRun || !Relative)
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

        if (untilLevel > PlayerCharacter.MAX_LEVEL)
        {
            untilLevel = PlayerCharacter.MAX_LEVEL;
        }

        logger.LogInformation(
            $"{JobName}: [{Character.Schema.Name}] run started - training {skillName} until level {untilLevel}"
        );

        SkillKind skillKind = SkillService.GetSkillKind(Skill);

        if (SkillLevel < untilLevel)
        {
            OneOf<List<CharacterJob>, AppError> result = await GetJobsRequired(
                Character,
                gameState,
                Skill,
                skillKind,
                SkillLevel
            );

            result.Switch(
                jobs =>
                {
                    if (jobs.Count > 0)
                    {
                        firstRun = false;
                        Character.QueueJobsBefore(Id, jobs);
                    }
                },
                _ => { }
            );
        }

        return new None();
    }

    public static async Task<OneOf<List<CharacterJob>, AppError>> GetJobsRequired(
        PlayerCharacter Character,
        GameState gameState,
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
                        if (!await Character.PlayerActionService.CanObtainItem(item))
                        {
                            continue;
                        }

                        if (bestItemToCraft is null || bestItemToCraft.Level < item.Level)
                        {
                            itemToCraftCandidates.Add(item);
                        }
                    }
                }

                var bankItemsResponse = await gameState.BankItemCache.GetBankItems(Character);

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

                        if (!resultA.CanObtain)
                        {
                            return 1;
                        }

                        int resultACost = resultA.Item2 + skillLevel - a.Craft!.Level;

                        var resultB = GetInconvenienceCostCraftItem(
                            b,
                            gameState,
                            bankItemsResponse.Data,
                            Character
                        );

                        int resultBCost = resultB.Item2 + skillLevel - b.Craft!.Level;

                        if (!resultB.CanObtain)
                        {
                            return -1;
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
                    if (skill == Skill.Alchemy)
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

                    return new AppError(
                        $"Could not find best item for training \"{skill}\" at skill level \"{skillLevel}\" for \"{Character.Schema.Name}\""
                    );
                }

                // Weapon, gear, and jewellery crafting often requires a lot of raw materials, which fill up the inventory faster.
                // Cooking and alchemy rarely do, so we can cook/make potions in bigger batches.
                int craftingAmount = 1;

                if (skill == Skill.Alchemy || skill == Skill.Cooking)
                {
                    craftingAmount = 20;
                }

                AppLogger
                    .GetLogger()
                    .LogInformation(
                        $"TrainSkill: [{Character.Schema.Name}] will be crafting {craftingAmount} x {bestItemToCraft.Code} to train {skill} until level {skillLevel}"
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
                    obtainItemJob.onSuccessEndHook = async () =>
                    {
                        AppLogger
                            .GetLogger()
                            .LogInformation(
                                $"TrainSkill: [{Character.Name}]: onSuccessEndHook: Adding job to recycle {craftingAmount} x {bestItemToCraft.Code}"
                            );
                        var recycleJob = new RecycleItem(
                            Character,
                            gameState,
                            bestItemToCraft.Code,
                            craftingAmount
                        ).SetParent<RecycleItem>(obtainItemJob);

                        recycleJob.ForBank();

                        await Character.QueueJob(recycleJob, true);
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

        return new AppError($"Could not find a way to train skill \"{skill}\" to {skillLevel}");
    }

    public static async Task<bool> CanDoJob(
        PlayerCharacter character,
        GameState gameState,
        Skill skill
    )
    {
        int skillLevel = character.GetSkillLevel(skill);

        SkillKind skillKind = SkillService.GetSkillKind(skill);

        var result = await GetJobsRequired(character, gameState, skill, skillKind, skillLevel);

        return result.Match(
            jobs =>
            {
                return jobs.Count > 0;
            },
            _ => false
        );
    }

    public static (bool CanObtain, int Score) GetInconvenienceCostCraftItem(
        ItemSchema item,
        GameState gameState,
        List<DropSchema> bankItems,
        PlayerCharacter character
    )
    {
        if (item.Craft is null)
        {
            // For now, we don't care about items that cannot be crafted - it's used to train skill
            return (false, 0);
        }

        var result = InnerGetInconvenienceCostCraftItem(item, 1, gameState, bankItems, character);

        return result;
    }

    public static (bool CanObtain, int Score) InnerGetInconvenienceCostCraftItem(
        ItemSchema item,
        int quantity,
        GameState gameState,
        List<DropSchema> bankItems,
        PlayerCharacter character
    )
    {
        int score = 0;
        bool canObtain = true;

        int amountInBank =
            bankItems
                .Find(bankItem => bankItem.Code == item.Code && bankItem.Quantity >= quantity)
                ?.Quantity ?? 0;

        if (amountInBank >= quantity)
        {
            return (true, 0);
        }

        var matchingItem = gameState.ItemsDict.GetValueOrNull(item.Code)!;

        if (matchingItem?.Subtype == "task")
        {
            score += 10 * quantity;
        }
        else if (matchingItem?.Subtype == "mob")
        {
            var monstersThatDropTheItem = gameState.AvailableMonsters.FindAll(monster =>
                monster.Drops.Find(drop => drop.Code == item.Code) is not null
            );

            MonsterSchema? monsterWeCanFight = null;
            FightOutcome? monsterWeCanFightOutcome = null;

            foreach (var monster in monstersThatDropTheItem)
            {
                if (
                    gameState.EventService.IsEntityFromEvent(monster.Code)
                    && gameState.EventService.WhereIsEntityActive(monster.Code) is null
                )
                {
                    continue;
                }
                var fightOutcome = FightSimulator
                    .FindBestFightEquipmentWithUsablePotions(character, gameState, monster)
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
                int dropRateFactor =
                    monsterWeCanFight.Drops.FirstOrDefault(drop => drop.Code == item.Code)!.Rate
                    * quantity;

                score +=
                    2
                    + (int)Math.Round((float)monsterWeCanFightOutcome!.TotalTurns / 10)
                        * dropRateFactor;
            }
            else
            {
                canObtain = false;
            }
        }
        else if (matchingItem!.Craft is not null)
        {
            foreach (var subComponent in matchingItem.Craft.Items)
            {
                var subComponentResult = InnerGetInconvenienceCostCraftItem(
                    gameState.ItemsDict[subComponent.Code],
                    subComponent.Quantity,
                    gameState,
                    bankItems,
                    character
                );

                if (!subComponentResult.CanObtain)
                {
                    return (false, score);
                }

                score += subComponentResult.Score;
            }
        }
        else
        {
            var resource = ItemService.FindBestResourceToGatherItem(
                character,
                gameState,
                item.Code
            );

            if (resource is not null)
            {
                var drop = resource.Drops.First(drop => drop.Code == item.Code)!;

                score += (int)Math.Floor(drop.Rate * (double)quantity);
            }
        }

        return (canObtain, score);
    }
}
