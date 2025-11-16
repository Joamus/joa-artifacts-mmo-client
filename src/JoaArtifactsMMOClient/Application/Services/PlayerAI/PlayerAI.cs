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
        var job =
            await GetEventJob()
            ?? await GetIndividualHighPrioJob()
            ?? GetRoleJob()
            ?? await GetIndividualLowPrioJob();

        logger.LogInformation($"{Name}: [{Character.Schema.Name}]: Found job - {job?.JobName}");
        return job!;
    }

    async Task<CharacterJob?> GetIndividualHighPrioJob()
    {
        logger.LogInformation(
            $"{Name}: [{Character.Schema.Name}]: GetIndividualHighPrioJob: Start"
        );
        var hasDoneItemTask =
            gameState.AccountAchievements.FirstOrDefault(achiev =>
                achiev.Code == "tasks_farmer" && achiev.CompletedAt is not null
            )
                is not null;
        // Evaluate if tools are up to date

        var bestTools = ItemService.GetBestTools(Character, gameState, null, hasDoneItemTask);

        if (!hasDoneItemTask)
        {
            logger.LogInformation(
                $"{Name}: [{Character.Schema.Name}]: GetIndividualHighPrioJob: Tasks farmer achievement is not completed yet - evaluating best tools, which don't require task materials"
            );
        }

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

            var item = gameState.ItemsDict.GetValueOrNull(
                tool.Code
            // itemInInventory!.Value.inventorySlot.Code
            )!;

            if (!await Character.PlayerActionService.CanObtainItem(item))
            {
                continue;
            }

            string itemCode = tool.Code;

            logger.LogInformation(
                $"{Name}: [{Character.Schema.Name}]: GetIndividualHighPrioJob: Job found - get {itemCode}"
            );

            var matchingItem = gameState.ItemsDict.GetValueOrNull(itemCode)!;

            int itemAmount = 1;

            return new ObtainOrFindItem(Character, gameState, tool.Code, itemAmount);
        }

        // Highest prio is completing this achievement, else all task items are locked.
        if (!hasDoneItemTask)
        {
            return await GetTaskJob(false);
        }

        if (Character.Schema.AlchemyLevel + SKILL_LEVEL_OFFSET <= Character.Schema.Level)
        {
            logger.LogInformation(
                $"{Name}: [{Character.Schema.Name}]: GetIndividualHighPrioJob: Training Alchemy - current level is {Character.Schema.AlchemyLevel}, compared to character level {Character.Schema.Level}"
            );
            return new TrainSkill(Character, gameState, Skill.Alchemy, 1, true);
        }

        if (Character.Schema.FishingLevel + SKILL_LEVEL_OFFSET <= Character.Schema.Level)
        {
            logger.LogInformation(
                $"{Name}: [{Character.Schema.Name}]: GetIndividualHighPrioJob: Training Fishing - current level is {Character.Schema.FishingLevel}, compared to character level {Character.Schema.Level}"
            );
            return new TrainSkill(Character, gameState, Skill.Fishing, 1, true);
        }

        if (Character.Schema.CookingLevel + SKILL_LEVEL_OFFSET <= Character.Schema.Level)
        {
            logger.LogInformation(
                $"{Name}: [{Character.Schema.Name}]: GetIndividualHighPrioJob: Training Cooking - current level is {Character.Schema.CookingLevel}, compared to character level {Character.Schema.Level}"
            );
            return new TrainSkill(Character, gameState, Skill.Cooking, 1, true);
        }

        // Evaluate if equipment is up to date

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
        if (Character.Schema.TaskType == TaskType.items.ToString())
        {
            logger.LogInformation(
                $"{Name}: [{Character.Schema.Name}]: GetIndividualLowPrioJob: Already has an item task - beginning/resuming item task"
            );
            return new ItemTask(Character, gameState);
        }
        else if (Character.Schema.TaskType == TaskType.monsters.ToString())
        {
            var jobs = await GetJobsToFightMonster(
                gameState.MonstersDict.GetValueOrNull(Character.Schema.Task)!
            );

            if (jobs.Count > 0)
            {
                var nextJob = jobs[0];

                logger.LogInformation(
                    $"{Name}: [{Character.Schema.Name}]: GetIndividualLowPrioJob: Doing first job to fight monster from monster task: {Character.Schema.TaskTotal - Character.Schema.TaskProgress} x {Character.Schema.Task} - job is {nextJob.JobName} for {nextJob.Amount} x {nextJob.Code}"
                );
                // Do the first job in the list, we only do one thing at a time
                return nextJob;
            }

            logger.LogInformation(
                $"{Name}: [{Character.Schema.Name}]: GetIndividualLowPrioJob: Falling back - could not do jobs to defeat monster \"{Character.Schema.Task}\" from monster task"
            );
        }
        else
        {
            logger.LogInformation(
                $"{Name}: [{Character.Schema.Name}]: GetIndividualLowPrioJob: Got no task - take monster task"
            );

            return new AcceptNewTask(Character, gameState, TaskType.monsters);
        }

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
                var jobs = await GetJobsToFightMonster(
                    gameState.MonstersDict.GetValueOrNull(fightMonster.Code)!
                );

                if (jobs.Count > 0)
                {
                    var nextJob = jobs[0];

                    logger.LogInformation(
                        $"{Name}: [{Character.Schema.Name}]: GetIndividualLowPrioJob: Doing first job to fight {fightMonster.Amount} x {fightMonster.Code} - job is {nextJob.JobName} for {nextJob.Amount} x {nextJob.Code}"
                    );
                    // Do the first job in the list, we only do one thing at a time
                    return nextJob;
                }
            }
        }

        logger.LogInformation(
            $"{Name}: [{Character.Schema.Name}]: GetIndividualLowPrioJob: Fallback job"
        );

        if (string.IsNullOrEmpty(Character.Schema.Task))
        {
            logger.LogInformation(
                $"{Name}: [{Character.Schema.Name}]: GetIndividualLowPrioJob: Fallback job - taking a new task"
            );
            return await GetTaskJob(false);
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
                    .CalculateFightOutcomeWithBestEquipment(Character, matchingMonster, gameState)
                    .ShouldFight
            )
            {
                return false;
            }
        }

        return true;
    }

    async Task<List<CharacterJob>?> GetJobsToFightMonster(MonsterSchema monster)
    {
        var jobsToGetItems = await Character.PlayerActionService.GetJobsToGetItemsToFightMonster(
            Character,
            gameState,
            monster
        );

        if (
            jobsToGetItems is null
            || jobsToGetItems.Count == 0
                && !FightSimulator
                    .CalculateFightOutcomeWithBestEquipment(Character, monster, gameState)
                    .ShouldFight
        )
        {
            return null;
        }

        return jobsToGetItems;
    }

    async Task<CharacterJob> GetTaskJob(bool preferMonsterTask = true)
    {
        if (Character.Schema.TaskType == TaskType.monsters.ToString())
        {
            var monster = gameState.MonstersDict.GetValueOrNull(Character.Schema.Task)!;
            var jobs = await GetJobsToFightMonster(monster);

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
            var jobsToFightMonster = await GetJobsToFightMonster(matchingMonster);

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
