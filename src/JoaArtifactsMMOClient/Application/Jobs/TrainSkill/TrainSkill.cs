using System.Collections.ObjectModel;
using System.Reflection.Metadata.Ecma335;
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
    const int MONSTER_COST = 5;
    const int CHARACTER_ABOVE_MONSTER_NEGATE_MONSTER_COST = 15;
    const int TASKS_COINS_COST = 120;
    const int EVENT_COST = 200;
    const int EVENT_COST_IF_HAS_ENOUGH_QUANTITY = 50;
    const int ENOUGH_EVENT_ITEMS = 1500;

    static readonly ReadOnlyCollection<string> MostExpensiveItemSubtypes = ["task", "event"];
    public static int AMOUNT_TO_GATHER_PER_JOB = 20;

    public static int LEVEL_DIFF_FOR_NO_XP = 10;

    public Skill Skill { get; init; }

    private string SkillName { get; set; }
    public int LevelOffset { get; private set; }

    public bool Relative { get; init; }

    public int SkillLevel { get; set; }

    bool FirstRun { get; set; } = true;

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
        SkillName = SkillService.GetSkillName(Skill);
    }

    protected override async Task<OneOf<AppError, None>> ExecuteAsync()
    {
        // Only runs the first time this job runs, if it's a relative level job. If it queues a job before itself, it shouldn't recalculate the level
        if (Relative && FirstRun || !Relative)
        {
            SkillLevel = Character.GetSkillLevel(SkillName);
        }

        int untilLevel;

        if (Relative)
        {
            untilLevel = SkillLevel + LevelOffset;

            if (!FirstRun && untilLevel <= Character.GetSkillLevel(SkillName))
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
            $"{JobName}: [{Character.Schema.Name}] run started - training {SkillName} until level {untilLevel}"
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
                        FirstRun = false;
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
                    if (
                        gameState.EventService.IsEntityFromEvent(resource.Code)
                        && gameState.EventService.WhereIsEntityActive(resource.Code) == null
                    )
                    {
                        continue;
                    }
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

                int maxXpForLevel = CraftingService.RawGetXpForCraftingItem(
                    skillLevel,
                    19,
                    skill,
                    Character.Schema.Wisdom
                );

                List<(ItemSchema Item, int Cost)> itemsWithCost =
                [
                    .. itemToCraftCandidates
                        .Select(
                            (item) =>
                            {
                                (bool CanObtain, int Cost) = (
                                    GetInconvenienceCostCraftItem(
                                        item,
                                        gameState,
                                        bankItemsResponse,
                                        Character
                                    )
                                );

                                // We want to bias toward the XP we would get.
                                int xpForCrafting = CraftingService.GetXpForCraftingItem(
                                    skillLevel,
                                    item,
                                    Character.Schema.Wisdom
                                );

                                float maxXpPotential = Math.Min(
                                    1.0f,
                                    (float)xpForCrafting / maxXpForLevel
                                );

                                int resultCost = (int)Math.Round(Cost / maxXpPotential);

                                return (item, CanObtain, Cost: resultCost);
                            }
                        )
                        .Where((result) => result.CanObtain)
                        .Select((result) => (result.item, result.Cost)),
                ];

                itemsWithCost.Sort((a, b) => a.Cost - b.Cost);

                bestItemToCraft = itemsWithCost.FirstOrDefault().Item;

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

        var result = InnerGetInconvenienceCostCraftItem(
            item,
            1,
            gameState,
            bankItems,
            character,
            true
        );

        return result;
    }

    public static (bool CanObtain, int Score) InnerGetInconvenienceCostCraftItem(
        ItemSchema item,
        int quantity,
        GameState gameState,
        List<DropSchema> bankItems,
        PlayerCharacter character,
        bool initialItem
    )
    {
        int cost = 0;
        bool canObtain = true;

        int amountInBank =
            bankItems
                .Find(bankItem => bankItem.Code == item.Code && bankItem.Quantity >= quantity)
                ?.Quantity ?? 0;

        var matchingItem = gameState.ItemsDict[item.Code];

        int eventCost =
            amountInBank > ENOUGH_EVENT_ITEMS ? EVENT_COST_IF_HAS_ENOUGH_QUANTITY : EVENT_COST;

        /**
        ** Basically we still want to consider task items and event items expensive, even if we have them in the bank
        */
        if (
            !initialItem
            && amountInBank >= quantity
            && !MostExpensiveItemSubtypes.Contains(matchingItem.Subtype)
        )
        {
            return (true, 0);
        }

        if (matchingItem.Subtype == "task")
        {
            int tasksCoinsPrice = gameState.NpcItemsDict[matchingItem.Code].BuyPrice ?? 1;
            cost += tasksCoinsPrice * TASKS_COINS_COST * quantity;
        }
        else if (matchingItem.Subtype == "mob")
        {
            var monstersThatDropTheItem = gameState.AvailableMonsters.FindAll(monster =>
                monster.Drops.Find(drop => drop.Code == item.Code) is not null
            );

            List<(MonsterSchema Monster, FightOutcome Outcome, int Cost)> qualifiedMonsters = [];

            foreach (var monster in monstersThatDropTheItem)
            {
                bool isEventMonster = gameState.EventService.IsEntityFromEvent(monster.Code);

                if (
                    isEventMonster
                    && gameState.EventService.WhereIsEntityActive(monster.Code) is null
                )
                {
                    continue;
                }
                var fightOutcome = FightSimulator
                    .FindBestFightEquipmentWithUsablePotions(character, gameState, monster)
                    .Outcome;

                if (fightOutcome.ShouldFight)
                {
                    int monsterCost = CalculateMonsterCost(
                        character,
                        monster,
                        isEventMonster,
                        item,
                        quantity,
                        fightOutcome,
                        eventCost
                    );

                    qualifiedMonsters.Add((monster, fightOutcome, monsterCost));
                }
            }

            qualifiedMonsters.Sort((a, b) => a.Cost - b.Cost);

            (MonsterSchema Monster, FightOutcome Outcome, int Cost)? monsterWeCanFight =
                qualifiedMonsters.FirstOrDefault();

            if (monsterWeCanFight is not null)
            {
                var monster = monsterWeCanFight.Value.Monster;

                cost += monsterWeCanFight.Value.Cost;
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
                    character,
                    false
                );

                if (!subComponentResult.CanObtain)
                {
                    return (false, cost);
                }

                cost += subComponentResult.Score;
            }
        }
        else
        {
            var resource = ItemService
                .FindBestResourceToGatherItem(character, gameState, item.Code)
                ?.Resource;

            if (resource is not null)
            {
                bool isFromEvent = gameState.EventService.IsEntityFromEvent(item.Code);

                var drop = resource.Drops.First(drop => drop.Code == item.Code)!;

                cost +=
                    (isFromEvent ? eventCost : 1) * (int)Math.Floor(drop.Rate * (double)quantity);
            }
        }

        return (canObtain, cost);
    }

    static int CalculateMonsterCost(
        PlayerCharacter character,
        MonsterSchema monster,
        bool isEventMonster,
        ItemSchema itemDrop,
        int itemAmount,
        FightOutcome fightOutcome,
        int eventCost
    )
    {
        int dropRateFactor =
            monster.Drops.FirstOrDefault(drop => drop.Code == itemDrop.Code)!.Rate * itemAmount;

        int monsterCost =
            monster.Level < character.Schema.Level + CHARACTER_ABOVE_MONSTER_NEGATE_MONSTER_COST
                ? 0
                : MONSTER_COST;

        if (isEventMonster)
        {
            monsterCost += eventCost;
        }

        int score =
            monsterCost + (int)Math.Round((float)fightOutcome!.TotalTurns / 10) * dropRateFactor;
        return score;
    }
}
