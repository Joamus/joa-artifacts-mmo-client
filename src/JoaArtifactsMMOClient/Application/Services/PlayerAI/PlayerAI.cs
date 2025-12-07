using System.Security.Permissions;
using System.Text.Json.Serialization;
using Application.Artifacts.Schemas;
using Application.ArtifactsApi.Schemas;
using Application.Character;
using Application.Dtos;
using Application.Errors;
using Application.Jobs;
using Applicaton.Services.FightSimulator;
using OneOf.Types;

namespace Application.Services;

public class PlayerAI
{
    private const string Name = "PlayerAI";
    private const int SKILL_LEVEL_OFFSET = 1;
    public PlayerCharacter Character { get; init; }

    public bool Enabled { get; set; } = true;

    private GameState gameState { get; set; }

    bool hasDoneItemTask { get; set; } = false;

    [JsonIgnore]
    public ILogger<CharacterJob> logger { get; init; } =
        AppLogger.loggerFactory.CreateLogger<CharacterJob>();

    public PlayerAI(PlayerCharacter character, GameState gameState, bool enabled = true)
    {
        Character = character;
        this.gameState = gameState;
        Enabled = enabled;
    }

    public async Task<CharacterJob> GetNextJob()
    {
        logger.LogInformation($"{Name}: [{Character.Schema.Name}]: Evaluating next job");

        hasDoneItemTask =
            gameState.AccountAchievements.FirstOrDefault(achiev =>
                achiev.Code == "tasks_farmer" && achiev.CompletedAt is not null
            )
                is not null;
        var job =
            await EnsureWeapon()
            ?? await GetEventJob()
            ?? await GetIndividualHighPrioJob()
            ?? await EnsureFightGear()
            ?? await EnsureBag()
            ?? GetSkillJob()
            ?? GetRoleJob()
            ?? await GetIndividualLowPrioJob();

        logger.LogInformation($"{Name}: [{Character.Schema.Name}]: Found job - {job?.JobName}");
        return job!;
    }

    async Task<CharacterJob?> EnsureFightGear()
    {
        var tasks = gameState.Tasks.ToList();

        // Loop from highest to lowest, and get the equipment we can get
        tasks.Sort((a, b) => b.Level - a.Level);

        MonsterSchema? firstMonsterWeCanFight = null;

        foreach (var task in tasks)
        {
            if (task.Type != TaskType.monsters)
            {
                continue;
            }

            if (task.Level <= Character.Schema.Level)
            {
                var matchingMonster = gameState.MonstersDict[task.Code]!;

                var fightSimResult = FightSimulator.FindBestFightEquipment(
                    Character,
                    gameState,
                    matchingMonster
                );

                if (!fightSimResult.Outcome.ShouldFight)
                {
                    var jobs = await FightSimulator.GetJobsToFightMonster(
                        Character,
                        gameState,
                        matchingMonster
                    );

                    if (jobs is null)
                    {
                        logger.LogInformation(
                            $"{Name}: [{Character.Schema.Name}]: EnsureFightGear: Cannot fight monster \"{matchingMonster.Code}\", but cannot get a list of jobs, to get the necessary items - skipping"
                        );
                        continue;
                    }

                    if (jobs.Count > 0)
                    {
                        var firstJob = jobs.ElementAt(0);

                        logger.LogInformation(
                            $"{Name}: [{Character.Schema.Name}]: EnsureFightGear: Cannot fight monster \"{matchingMonster.Code}\", but found a list of {jobs.Count} x jobs, to get the necessary items - obtaining or finding \"{firstJob.Code}\""
                        );

                        return firstJob;
                    }
                    logger.LogInformation(
                        $"{Name}: [{Character.Schema.Name}]: EnsureFightGear: Cannot fight monster \"{matchingMonster.Code}\", but found no jobs, to get the necessary items - skipping"
                    );

                    continue;
                }

                if (firstMonsterWeCanFight is null)
                {
                    firstMonsterWeCanFight = matchingMonster;
                }
            }
        }

        if (firstMonsterWeCanFight is not null)
        {
            if (Character.Schema.Level - firstMonsterWeCanFight.Level < 5)
            {
                // we are probably good? We should have up to date gear
                return null;
            }

            logger.LogInformation(
                $"{Name}: [{Character.Schema.Name}]: EnsureFightGear: Fallback - the highest level monster we can fight is \"{firstMonsterWeCanFight.Code}\" - trying to get jobs to fight it"
            );

            var jobs = await FightSimulator.GetJobsToFightMonster(
                Character,
                gameState,
                firstMonsterWeCanFight
            );

            if (jobs is null)
            {
                logger.LogInformation(
                    $"{Name}: [{Character.Schema.Name}]: EnsureFightGear: Fallback - cannot fight monster \"{firstMonsterWeCanFight.Code}\", but cannot get a list of jobs, to get the necessary items - skipping"
                );
                return null;
            }

            if (jobs.Count > 0)
            {
                var firstJob = jobs.ElementAt(0);

                logger.LogInformation(
                    $"{Name}: [{Character.Schema.Name}]: EnsureFightGear: Fallback - cannot fight monster \"{firstMonsterWeCanFight.Code}\", but found a list of {jobs.Count} x jobs, to get the necessary items - obtaining or finding \"{firstJob.Code}\""
                );

                return firstJob;
            }
            logger.LogInformation(
                $"{Name}: [{Character.Schema.Name}]: EnsureFightGear: Fallback - cannot fight monster \"{firstMonsterWeCanFight.Code}\", but found no jobs, to get the necessary items - skipping"
            );
        }

        return null;
    }

    async Task<CharacterJob?> EnsureBag()
    {
        // All bags need task crystals
        if (!hasDoneItemTask)
        {
            return null;
        }
        var tasks = gameState.Tasks;

        // A bit of a hack - we know that satchel is the first bag item,
        // so we just want to return early if our characters cannot even use it
        var satchel = gameState.ItemsDict["satchel"]!;

        if (!ItemService.CanUseItem(satchel, Character.Schema))
        {
            return null;
        }

        var bagItems = gameState.Items.FindAll(item => item.Type == "bag").ToList();

        // Take highest level first, and prioritize seeing if we can equip those
        bagItems.Sort((a, b) => b.Level - a.Level);

        var equippedBag = gameState.ItemsDict.GetValueOrNull(Character.Schema.BagSlot);

        foreach (var item in bagItems)
        {
            if (!ItemService.CanUseItem(item, Character.Schema))
            {
                continue;
            }

            var result = Character.GetEquippedItemOrInInventory(item.Code);

            (InventorySlot inventorySlot, bool isEquipped)? itemInInventory =
                result.Count > 0 ? result.ElementAt(0)! : null;

            if (itemInInventory is not null)
            {
                continue;
            }

            if (equippedBag is not null)
            {
                // We can be cheeky - level is probably the easiest way to determine which bag is better
                if (equippedBag.Level >= item.Level)
                {
                    return null;
                }
            }

            var otherBagsInInventory = Character.GetItemsFromInventoryWithType("bag");

            foreach (var inventoryBag in otherBagsInInventory)
            {
                if (!ItemService.CanUseItem(inventoryBag.Item, Character.Schema))
                {
                    continue;
                }

                await Character.EquipItem(inventoryBag.Item.Code, "bag", 1);
                continue;
                // var bestUpgrade = ItemService.GetBestItemIfUpgrade(inventoryBag.Item, item);

                // if (bestUpgrade is not null)
                // {
                //     if (bestUpgrade.Code == inventoryBag.Item.Code)
                //     {
                //         // No reason for us to just have it in our inventory - this code will run again, if the bag isn't the best
                //         await Character.EquipItem(inventoryBag.Item.Code, "bag", 1);
                //         return null;
                //     }
                // }
            }

            if (!await Character.PlayerActionService.CanObtainItem(item))
            {
                continue;
            }

            return new ObtainOrFindItem(Character, gameState, item.Code, 1);
        }

        return null;
    }

    async Task<CharacterJob?> EnsureWeapon()
    {
        var currentWeapon = gameState.ItemsDict.GetValueOrNull(Character.Schema.WeaponSlot);

        if (currentWeapon is not null && currentWeapon.Subtype != "tool")
        {
            return null;
        }

        var weaponsInInventory = Character.GetItemsFromInventoryWithType("weapon");

        bool hasUsableWeapon = weaponsInInventory.Exists(weapon =>
            weapon.Item.Subtype != "tool" && ItemService.CanUseItem(weapon.Item, Character.Schema)
        );

        if (hasUsableWeapon)
        {
            return null;
        }

        var bankItems = await gameState.BankItemCache.GetBankItems(Character);

        ItemSchema? bestCandidate = null;

        foreach (var item in bankItems.Data)
        {
            var matchingItem = gameState.ItemsDict[item.Code];

            if (matchingItem.Type != "weapon" || matchingItem.Subtype == "tool")
            {
                continue;
            }

            if (!ItemService.CanUseItem(matchingItem, Character.Schema))
            {
                continue;
            }

            if (bestCandidate is null || matchingItem.Level > bestCandidate.Level)
            {
                bestCandidate = matchingItem;
            }
        }

        if (bestCandidate is not null)
        {
            logger.LogInformation(
                $"{Name}: [{Character.Schema.Name}]: Current weapon was {currentWeapon?.Code ?? "n/a"} - withdrawing 1 x {bestCandidate.Code}"
            );
            return new WithdrawItem(Character, gameState, bestCandidate.Code, 1, false);
        }

        logger.LogInformation(
            $"{Name}: [{Character.Schema.Name}]: Could not find weapon from bank - cannot handle at the moment"
        );

        return null;
    }

    async Task<CharacterJob?> GetIndividualHighPrioJob()
    {
        logger.LogInformation(
            $"{Name}: [{Character.Schema.Name}]: GetIndividualHighPrioJob: Start"
        );

        var bestTools = await ItemService.GetBestTools(Character, gameState, null, hasDoneItemTask);

        if (!hasDoneItemTask)
        {
            logger.LogInformation(
                $"{Name}: [{Character.Schema.Name}]: GetIndividualHighPrioJob: Tasks farmer achievement is not completed yet - evaluating best tools, which don't require task materials"
            );
        }

        ItemSchema? equippedWeapon = gameState.ItemsDict.GetValueOrNull(
            Character.Schema.WeaponSlot
        );

        foreach (var tool in bestTools)
        {
            var result = Character.GetEquippedItemOrInInventory(tool.Code);

            (InventorySlot inventorySlot, bool isEquipped)? itemInInventory =
                result.Count > 0 ? result.ElementAt(0)! : null;

            if (itemInInventory is not null)
            {
                continue;
            }

            if (Character.ExistsInWishlist(tool.Code))
            {
                logger.LogInformation(
                    $"{Name}: [{Character.Schema.Name}]: GetIndividualHighPrioJob: Skipping obtaining tool {tool.Code} - is already in wish list"
                );
                continue;
            }

            if (!await Character.PlayerActionService.CanObtainItem(tool))
            {
                continue;
            }

            if (equippedWeapon is not null)
            {
                var bestUpgrade = ItemService.GetBestItemIfUpgrade(equippedWeapon, tool);

                if (bestUpgrade is not null)
                {
                    if (bestUpgrade.Code == equippedWeapon.Code)
                    {
                        // We have a better tool, so don't care about getting another one
                        continue;
                    }
                }
            }

            var otherToolsInInventory = Character.GetItemsFromInventoryWithSubtype("tool");

            bool hasBetterTool = false;

            foreach (var inventoryTool in otherToolsInInventory)
            {
                if (!ItemService.CanUseItem(inventoryTool.Item, Character.Schema))
                {
                    continue;
                }

                var bestUpgrade = ItemService.GetBestItemIfUpgrade(inventoryTool.Item, tool);

                if (bestUpgrade is not null)
                {
                    if (bestUpgrade.Code == inventoryTool.Item.Code)
                    {
                        // We have a better tool, so don't care about getting another one
                        hasBetterTool = true;
                        break;
                    }
                }
            }

            if (hasBetterTool)
            {
                continue;
            }

            string itemCode = tool.Code;

            logger.LogInformation(
                $"{Name}: [{Character.Schema.Name}]: GetIndividualHighPrioJob: Job found - get {itemCode}"
            );

            int itemAmount = 1;

            return new ObtainOrFindItem(Character, gameState, tool.Code, itemAmount);
        }

        // Highest prio is completing this achievement, else all task items are locked.
        if (!hasDoneItemTask)
        {
            return await GetTaskJob(false);
        }

        return null;
    }

    CharacterJob? GetSkillJob()
    {
        if (Character.Schema.FishingLevel + SKILL_LEVEL_OFFSET <= Character.Schema.Level)
        {
            logger.LogInformation(
                $"{Name}: [{Character.Schema.Name}]: GetSkillJob: Training Fishing - current level is {Character.Schema.FishingLevel}, compared to character level {Character.Schema.Level}"
            );
            return new TrainSkill(Character, gameState, Skill.Fishing, 1, true);
        }

        if (Character.Schema.CookingLevel + SKILL_LEVEL_OFFSET <= Character.Schema.Level)
        {
            logger.LogInformation(
                $"{Name}: [{Character.Schema.Name}]: GetSkillJob: Training Cooking - current level is {Character.Schema.CookingLevel}, compared to character level {Character.Schema.Level}"
            );
            return new TrainSkill(Character, gameState, Skill.Cooking, 1, true);
        }

        if (Character.Schema.AlchemyLevel + SKILL_LEVEL_OFFSET <= Character.Schema.Level)
        {
            logger.LogInformation(
                $"{Name}: [{Character.Schema.Name}]: GetSkillJob: Training Alchemy - current level is {Character.Schema.AlchemyLevel}, compared to character level {Character.Schema.Level}"
            );
            return new TrainSkill(Character, gameState, Skill.Alchemy, 1, true);
        }

        return null;
    }

    CharacterJob? GetRoleJob()
    {
        foreach (var role in Character.Roles)
        {
            switch (role)
            {
                case Skill.Weaponcrafting:
                    if (
                        Character.Schema.WeaponcraftingLevel + SKILL_LEVEL_OFFSET
                        <= Character.Schema.Level
                    )
                    {
                        logger.LogInformation(
                            $"{Name}: [{Character.Schema.Name}]: GetRoleJob: Training Weaponcrafting - current level is {Character.Schema.WeaponcraftingLevel}, compared to character level {Character.Schema.Level}"
                        );
                        return new TrainSkill(Character, gameState, Skill.Weaponcrafting, 1, true);
                    }
                    break;
                case Skill.Gearcrafting:
                    if (
                        Character.Schema.GearcraftingLevel + SKILL_LEVEL_OFFSET
                        <= Character.Schema.Level
                    )
                    {
                        logger.LogInformation(
                            $"{Name}: [{Character.Schema.Name}]: GetRoleJob: Training Gearcrafting - current level is {Character.Schema.GearcraftingLevel}, compared to character level {Character.Schema.Level}"
                        );
                        return new TrainSkill(Character, gameState, Skill.Gearcrafting, 1, true);
                    }
                    break;
                case Skill.Jewelrycrafting:
                    if (
                        Character.Schema.JewelrycraftingLevel + SKILL_LEVEL_OFFSET
                        <= Character.Schema.Level
                    )
                    {
                        logger.LogInformation(
                            $"{Name}: [{Character.Schema.Name}]: GetRoleJob: Training Jewelrycrafting - current level is {Character.Schema.JewelrycraftingLevel}, compared to character level {Character.Schema.Level}"
                        );
                        return new TrainSkill(Character, gameState, Skill.Jewelrycrafting, 1, true);
                    }
                    break;
            }
        }

        return null;
    }

    async Task<CharacterJob> GetIndividualLowPrioJob()
    {
        bool hasNoTask = string.IsNullOrWhiteSpace(Character.Schema.Task);

        if (Character.Schema.TaskType == TaskType.items.ToString())
        {
            logger.LogInformation(
                $"{Name}: [{Character.Schema.Name}]: GetIndividualLowPrioJob: Already has an item task - beginning/resuming item task"
            );
            return new ItemTask(Character, gameState);
        }
        else if (Character.Schema.TaskType == TaskType.monsters.ToString())
        {
            var jobs = await FightSimulator.GetJobsToFightMonster(
                Character,
                gameState,
                gameState.MonstersDict.GetValueOrNull(Character.Schema.Task)!
            );

            if (jobs is not null)
            {
                if (jobs.Count > 0)
                {
                    var nextJob = jobs[0];

                    logger.LogInformation(
                        $"{Name}: [{Character.Schema.Name}]: GetIndividualLowPrioJob: Doing first job to fight monster from monster task: {Character.Schema.TaskTotal - Character.Schema.TaskProgress} x {Character.Schema.Task} - job is {nextJob.JobName} for {nextJob.Amount} x {nextJob.Code}"
                    );
                    // Do the first job in the list, we only do one thing at a time
                    return nextJob;
                }
                // else
                // {
                // Monster tasks aren't that good, because you can get monsters that don't give XP.
                // return new MonsterTask(Character, gameState);
                // }
            }
            else
            {
                logger.LogInformation(
                    $"{Name}: [{Character.Schema.Name}]: GetIndividualLowPrioJob: Falling back - could not do jobs to defeat monster \"{Character.Schema.Task}\" from monster task"
                );
            }
        }
        // else
        // {
        //     logger.LogInformation(
        //         $"{Name}: [{Character.Schema.Name}]: GetIndividualLowPrioJob: Got no task - take item task"
        //     );

        //     return new AcceptNewTask(Character, gameState, TaskType.items);
        // }

        logger.LogInformation(
            $"{Name}: [{Character.Schema.Name}]: GetIndividualLowPrioJob: Fall through - cannot handle the current task of type \"{Character.Schema.TaskType}\" for {Character.Schema.Task}"
        );

        // A bit dirty - we first want to try to find something to fight, allowing the character get better equipment.
        // After that, we will try without allowing it, in case items are on the wish list.
        foreach (var flag in new List<bool> { false, true })
        {
            var fightMonster = TrainCombat.GetJobRequired(
                Character,
                gameState,
                Character.Schema.Level,
                flag
            );

            if (fightMonster is not null)
            {
                logger.LogInformation(
                    $"{Name}: [{Character.Schema.Name}]: GetIndividualLowPrioJob: Finding a train combat job - fighting  {fightMonster.Amount} x {fightMonster.Code}"
                );
                var jobs = await FightSimulator.GetJobsToFightMonster(
                    Character,
                    gameState,
                    gameState.MonstersDict.GetValueOrNull(fightMonster.Code)!
                );

                if (jobs is not null)
                {
                    if (jobs.Count > 0)
                    {
                        var nextJob = jobs[0];

                        logger.LogInformation(
                            $"{Name}: [{Character.Schema.Name}]: GetIndividualLowPrioJob: Doing first job to fight {fightMonster.Amount} x {fightMonster.Code} - job is {nextJob.JobName} for {nextJob.Amount} x {nextJob.Code}"
                        );
                        // Do the first job in the list, we only do one thing at a time
                        return nextJob;
                    }
                    else
                    {
                        logger.LogInformation(
                            $"{Name}: [{Character.Schema.Name}]: GetIndividualLowPrioJob: Fighting {fightMonster.Amount} x {fightMonster.Code}"
                        );
                        return fightMonster;
                    }
                }
            }
        }

        logger.LogInformation(
            $"{Name}: [{Character.Schema.Name}]: GetIndividualLowPrioJob: Fallback job"
        );

        // if (string.IsNullOrEmpty(Character.Schema.Task))
        // {
        //     logger.LogInformation(
        //         $"{Name}: [{Character.Schema.Name}]: GetIndividualLowPrioJob: Fallback job - taking a new task"
        //     );
        //     return await GetTaskJob(false);
        // }


        if (hasNoTask)
        {
            logger.LogInformation(
                $"{Name}: [{Character.Schema.Name}]: GetIndividualLowPrioJob: Got no task - take item task"
            );

            return new AcceptNewTask(Character, gameState, TaskType.items);
        }

        logger.LogInformation(
            $"{Name}: [{Character.Schema.Name}]: GetIndividualLowPrioJob: Fallback job - leveling mining"
        );
        return new TrainSkill(Character, gameState, Skill.Mining, 1, true);
    }

    public async Task GetCrafterJob()
    {
        await Task.Run(() => { });
    }

    public bool CanHandlePotentialMonsterTasks()
    {
        foreach (var task in gameState.Tasks)
        {
            if (task.Type != TaskType.monsters)
            {
                continue;
            }

            var matchingMonster = gameState.MonstersDict.GetValueOrNull(task.Code)!;

            if (matchingMonster.Level > Character.Schema.Level)
            {
                continue;
            }

            if (
                !FightSimulator
                    .FindBestFightEquipmentWithUsablePotions(Character, gameState, matchingMonster)
                    .Outcome.ShouldFight
            )
            {
                return false;
            }
        }

        return true;
    }

    async Task<CharacterJob> GetTaskJob(bool preferMonsterTask = true)
    {
        if (Character.Schema.TaskType == TaskType.monsters.ToString())
        {
            var monster = gameState.MonstersDict.GetValueOrNull(Character.Schema.Task)!;
            var jobs = await FightSimulator.GetJobsToFightMonster(Character, gameState, monster);

            if (jobs is not null)
            {
                if (jobs.Count > 0)
                {
                    logger.LogInformation(
                        $"{Name}: [{Character.Schema.Name}]: GetIndividualHighPrioJob: Job found - do monster task ({monster.Code})"
                    );

                    var nextJob = jobs[0];

                    logger.LogInformation(
                        $"{Name}: [{Character.Schema.Name}]: GetIndividualHighPrioJob: Doing first job to fight job for monster task - fighting {Character.Schema.TaskTotal - Character.Schema.TaskProgress} x {monster.Code} - job is {nextJob.JobName} for {nextJob.Amount} x {nextJob.Code}"
                    );
                    // Do the first job in the list, we only do one thing at a time
                    return nextJob;
                }
                else
                {
                    logger.LogInformation(
                        $"{Name}: [{Character.Schema.Name}]: GetIndividualHighPrioJob: No items left to get to do monster task - fighting {Character.Schema.TaskTotal - Character.Schema.TaskProgress} x {monster.Code}"
                    );
                    new MonsterTask(Character, gameState);
                }
            }
        }
        return preferMonsterTask && CanHandlePotentialMonsterTasks()
            ? new MonsterTask(Character, gameState)
            : new ItemTask(Character, gameState);
    }

    async Task<CharacterJob?> GetEventJob()
    {
        var activeEvents = gameState.EventService.ActiveEvents;

        if (activeEvents.Count == 0)
        {
            return null;
        }

        logger.LogInformation(
            $"{Name}: [{Character.Schema.Name}]: GetEventJob: Evaluating active events - there are {activeEvents.Count} active events"
        );

        foreach (var activeEvent in activeEvents)
        {
            var gameEvent = gameState.EventService.EventsDict.GetValueOrNull(activeEvent.Code)!;

            var eventContent = gameEvent.Content;

            CharacterJob? job = null;

            switch (eventContent.Type)
            {
                case ContentType.Monster:
                    job = await GetMonsterEventJob(eventContent);
                    break;
                case ContentType.Npc:
                    job = await GetNpcEventJob(eventContent);
                    break;
                case ContentType.Resource:
                    job = await GetResourceEventJob(eventContent);
                    break;
            }

            if (job is not null)
            {
                return job;
            }
        }

        logger.LogInformation(
            $"{Name}: [{Character.Schema.Name}]: GetEventJob: No event job scheduled out of {activeEvents.Count} active events"
        );

        return null;
    }

    async Task<CharacterJob?> GetMonsterEventJob(MapContentSchema eventContent)
    {
        var matchingMonster = gameState.MonstersDict.GetValueOrNull(eventContent.Code);

        if (matchingMonster is not null && matchingMonster.Level <= Character.Schema.Level)
        {
            var jobsToFightMonster = await FightSimulator.GetJobsToFightMonster(
                Character,
                gameState,
                matchingMonster
            );

            if (jobsToFightMonster is not null)
            {
                if (jobsToFightMonster.Count > 0)
                {
                    var nextJob = jobsToFightMonster[0];

                    logger.LogInformation(
                        $"{Name}: [{Character.Schema.Name}]: GetEventJob: Doing first job to fight event monster - job is {nextJob.JobName} for {nextJob.Amount} x {nextJob.Code}"
                    );
                    return nextJob;
                }
                else
                {
                    logger.LogInformation(
                        $"{Name}: [{Character.Schema.Name}]: GetIndividualHighPrioJob: No items left to get to do fight event monster - fighting {TrainCombat.AMOUNT_TO_KILL} x {matchingMonster.Code}"
                    );
                    return new FightMonster(
                        Character,
                        gameState,
                        matchingMonster.Code,
                        TrainCombat.AMOUNT_TO_KILL
                    );
                }
            }
        }

        return null;
    }

    async Task<CharacterJob?> GetNpcEventJob(MapContentSchema eventContent)
    {
        return null;
    }

    async Task<CharacterJob?> GetResourceEventJob(MapContentSchema eventContent)
    {
        var matchingResource = gameState.Resources.FirstOrDefault(resource =>
            resource.Code == eventContent.Code
        );

        if (matchingResource is not null)
        {
            if (GatherResourceItem.CanGatherResource(matchingResource, Character.Schema))
            {
                // A bit wonky, but we don't gather a resource per se, we just try to get the drops from the resource.
                // We assume that an event is the only way to gather this resource
                return new GatherResourceItem(
                    Character,
                    gameState,
                    matchingResource.Drops.ElementAt(0).Code,
                    TrainSkill.AMOUNT_TO_GATHER_PER_JOB,
                    false
                );
            }
        }
        return null;
    }
}
