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
            await GetIndividualHighPrioJob() ?? GetRoleJob() ?? await GetIndividualLowPrioJob();

        logger.LogInformation($"{Name}: [{Character.Schema.Name}]: Found job - {job?.JobName}");
        return job!;
    }

    async Task<CharacterJob?> GetIndividualHighPrioJob()
    {
        logger.LogInformation(
            $"{Name}: [{Character.Schema.Name}]: GetIndividualHighPrioJob: Start"
        );
        var itemTaskAchievement = gameState.AccountAchievements.FirstOrDefault(achiev =>
            achiev.Code == "tasks_farmer" && achiev.CompletedAt is not null
        );
        // Evaluate if tools are up to date

        var bestTools = ItemService.GetBestTools(
            Character,
            gameState,
            null,
            itemTaskAchievement is null ? false : true
        );

        if (itemTaskAchievement is null)
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

            if (!await CanObtainItem(item))
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
        if (itemTaskAchievement is null)
        {
            if (Character.Schema.TaskType == TaskType.monsters.ToString())
            {
                var monster = gameState.Monsters.FirstOrDefault(monster =>
                    monster.Code == Character.Schema.Task
                )!;
                if (
                    FightSimulator
                        .CalculateFightOutcomeWithBestEquipment(Character, monster, gameState)
                        .ShouldFight
                )
                {
                    logger.LogInformation(
                        $"{Name}: [{Character.Schema.Name}]: GetIndividualHighPrioJob: Job found - do monster task ({monster.Code})"
                    );
                    return new MonsterTask(Character, gameState);
                }
                else
                {
                    logger.LogInformation(
                        $"{Name}: [{Character.Schema.Name}]: GetIndividualHighPrioJob: Has a monster task, but cannot win a fight against {monster.Code} - moving on"
                    );
                }
            }
            else
            {
                logger.LogInformation(
                    $"{Name}: [{Character.Schema.Name}]: GetIndividualHighPrioJob: Job found - do item task"
                );
                return new ItemTask(Character, gameState);
            }
        }

        logger.LogInformation(
            $"{Name}: [{Character.Schema.Name}]: GetIndividualHighPrioJob: Evaluating whether I should get better fight equipment"
        );

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
        else
        {
            logger.LogInformation(
                $"{Name}: [{Character.Schema.Name}]: GetIndividualLowPrioJob: Doing monster task for XP"
            );

            if (CanHandlePotentialMonsterTasks())
            {
                logger.LogInformation(
                    $"{Name}: [{Character.Schema.Name}]: GetIndividualLowPrioJob: Can handle all potential monster tasks - doing monster task"
                );
                return new MonsterTask(Character, gameState);
            }
            else
            {
                var fightMonster = await TrainCombat.GetJobRequired(
                    Character,
                    gameState,
                    Character.Schema.Level
                );

                if (fightMonster is not null)
                {
                    logger.LogInformation(
                        $"{Name}: [{Character.Schema.Name}]: GetIndividualLowPrioJob: Cannot handle all potential monster tasks - fighting  {fightMonster.Amount} x {fightMonster.Code}"
                    );
                    var jobs = await GetJobsToFightMonster(fightMonster);

                    if (jobs is not null)
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
        }

        logger.LogInformation(
            $"{Name}: [{Character.Schema.Name}]: GetIndividualLowPrioJob: Fallback job - this should never happen - just doing an item task"
        );
        return new ItemTask(Character, gameState);
        // Fallback job - should never happen
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

            var matchingMonster = gameState.Monsters.First(monster => monster.Code == task.Code)!;

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

    async Task<List<CharacterJob>> GetJobsToFightMonster(FightMonster fightMonster)
    {
        var matchingMonster = gameState.Monsters.FirstOrDefault(monster =>
            monster.Code == fightMonster.Code
        );

        if (matchingMonster is null)
        {
            throw new AppError(
                $"Monster with code {fightMonster.Code} could not be found in gameState.Monsters"
            );
        }

        var jobsToGetItems = await Character.PlayerActionService.GetJobsToGetItemsToFightMonster(
            Character,
            gameState,
            matchingMonster
        );

        if (jobsToGetItems is null)
        {
            return [];
        }

        jobsToGetItems.Append(fightMonster);

        return jobsToGetItems;
    }

    public async Task<bool> CanObtainItem(ItemSchema item)
    {
        var canObtainIt = await ObtainItem.GetJobsRequired(
            Character,
            gameState,
            true,
            [],
            [],
            item.Code,
            1,
            true,
            true
        );

        switch (canObtainIt.Value)
        {
            case AppError:
                return false;
        }

        return true;
    }
}
