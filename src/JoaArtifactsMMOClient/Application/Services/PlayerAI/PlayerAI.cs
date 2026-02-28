using System.Text.Json.Serialization;
using Application.Artifacts.Schemas;
using Application.ArtifactsApi.Schemas;
using Application.Character;
using Application.Dtos;
using Application.Jobs;
using Application.Jobs.Chores;
using Applicaton.Jobs.Chores;
using Applicaton.Services.FightSimulator;
using Microsoft.OpenApi.Extensions;

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
            ?? await GetChoreJob()
            ?? await GetIndividualHighPrioJob()
            // ?? await EnsureFightGear()
            ?? await EnsureBag()
            ?? GetSkillJob()
            ?? await GetRoleJob()
            ?? await GetIndividualLowPrioJob();

        logger.LogInformation($"{Name}: [{Character.Schema.Name}]: Found job - {job?.JobName}");
        return job!;
    }

    async Task<CharacterJob?> EnsureAccessories()
    {
        /* Check if the character has empty slots (start with artifacts), that might not be related to combat (because they give prospecting/wisdom),
         * purchase them if possible (later on artifacts will give combat stats)
         *
         * For now, we just want to ensure that the artifact slots aren't empty.
         * The logic is a bit simple for now, but we just try to gather unique artifacts in eacah slot, if possible
         *
         *
        */
        List<ItemSchema> nonCombatArtifacts = gameState
            .Items.Where(item =>
                // Don't really care for the seasonal stuff at the moment, but maybe we should
                item.Type == "artifact"
                && ItemService.CanUseItem(item, Character.Schema)
                && !item.Name.Contains("Christmas")
            )
            .ToList();

        if (nonCombatArtifacts.Count == 0)
        {
            return null;
        }

        List<(string ItemCode, string Slot)> slots =
        [
            (Character.Schema.Artifact1Slot, "Artifact1Slot"),
            (Character.Schema.Artifact2Slot, "Artifact2Slot"),
            (Character.Schema.Artifact3Slot, "Artifact3Slot"),
        ];

        var bankItems = await gameState.BankItemCache.GetBankItems(Character);

        var bankItemsDict = bankItems.Data.ToDictionary((item) => item.Code);

        foreach (var slot in slots)
        {
            // For now, only bother getting artifacts for empty slots. Later on, most artifacts are combat related anyway, so we will automatically find better artifacts then.
            if (!string.IsNullOrWhiteSpace(slot.ItemCode))
            {
                continue;
            }

            foreach (var artifact in nonCombatArtifacts)
            {
                var result = Character.GetEquippedItemOrInInventory(artifact.Code);

                (InventorySlot inventorySlot, bool isEquipped)? itemInInventory =
                    result.Count > 0 ? result.ElementAt(0)! : null;

                if (itemInInventory is not null && !itemInInventory.Value.isEquipped)
                {
                    await Character.EquipItem(
                        itemInInventory.Value.inventorySlot.Code,
                        slot.Slot.FromPascalToSnakeCase(),
                        1
                    );
                    return null;
                }

                var matchInBank = bankItemsDict.GetValueOrNull(artifact.Code);

                if (matchInBank is not null)
                {
                    var withdrawItem = new WithdrawItem(Character, gameState, artifact.Code, 1);

                    withdrawItem.onAfterSuccessEndHook = async () =>
                    {
                        await Character.EquipItem(
                            artifact.Code,
                            slot.Slot.FromPascalToSnakeCase(),
                            1
                        );
                    };

                    return withdrawItem;
                }

                if (!await Character.PlayerActionService.CanObtainItem(artifact))
                {
                    continue;
                }

                /**
                ** TODO: This is very rudimentary artifact logic - we for now just want unique non-combat artifacts,
                ** hoping that it will give us well-banaced characters. In the future, we should optimize for having
                ** the best possible non-combat artifacts for specific jobs.
                */
                if (slots.Exists(slot => slot.ItemCode == artifact.Code))
                {
                    continue;
                }

                var job = new ObtainOrFindItem(Character, gameState, artifact.Code, 1);

                job.onAfterSuccessEndHook = async () =>
                {
                    await Character.EquipItem(artifact.Code, slot.Slot.FromPascalToSnakeCase(), 1);
                };
            }
        }

        return null;
    }

    async Task<CharacterJob?> RecycleOldItems()
    {
        /*
         * Recycle items that are no longer relevant (check if)
         * - Check if recycleable, and more than 13-ish lvls below the lowest lvl char
         * - Could techically check if the item is not the BiS item for any relevant monsters for all characters, but probably not needed
         * - We can still recycle better items, as long as we have a minimum of 5 in the bank (10 for rings), so all chars can get one if needed
         * - To keep it easy, let only e.g. the wep crafter recycle weapons, gear crafter recycle gear, etc.

        */
        return null;
    }

    async Task<CharacterJob?> SellItems()
    {
        /*
         * Sell items, primarily focused on items that have no other purpose (craft is null, and no effects).
         * - Loop through all NPCs, check they are active if needed, and for each NPC, find all items in the bank (or inventory) that we can sell
         * - Can be expanded to sell materials that might have purpose, but are not used in any item tasks, and is 13-ish lvls below lowest char lvl.
         *   e.g, sell old ore, wood, etc., if they are never used in item tasks, and we won't need them.
            This can be expanded to selling items on the GE

        */
        return null;
    }

    async Task<CharacterJob?> RestockOnPurchasableItems()
    {
        /*
         * Restock on select items, that can be purchased
         * - Restock from a static list of items, could be recall potions, etc. Could enforce some conditions, to only restock if relevant.
         * - Would work something like "restock on recall potions, if we have below 10 in our bank, then restock up until 100"
         * - Can be expanded to allow buying materials for gold if available, e.g. buying algae instead of gathering it, buying copper ore.
         *   This is a bit more advanced, because it's difficult to know when we want to do that.
         *   It could depend on how much gold each character is allowed to spend, e.g. don't spend more than x% of your total gold.
         *   But it could significantly accelerate progression. It could in the future also allow for purchasing from the GE.

        */
        return null;
    }

    async Task<CharacterJob?> RestockOnCraftItems()
    {
        /*
         * Restock on select
         * - Loop through all NPCs, check they are active if needed, and for each NPC, find all items in the bank (or inventory) that we can sell
         * - Can be expanded to sell materials that might have purpose, but are not used in any item tasks, and is 13-ish lvls below lowest char lvl.

        */
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

        var bankItems = await gameState.BankItemCache.GetBankItems(Character);

        var bankItemsDict = bankItems.Data.ToDictionary((item) => item.Code);

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
                if (!itemInInventory.Value.isEquipped)
                {
                    await Character.EquipItem(itemInInventory.Value.inventorySlot.Code, "bag", 1);
                }
                continue;
            }

            if (equippedBag is not null)
            {
                // We can be cheeky - level is probably the easiest way to determine which bag is better
                // could also look at the inventory space effect, but eh...
                if (equippedBag.Level >= item.Level)
                {
                    return null;
                }
            }

            var matchInBank = bankItemsDict.GetValueOrNull(item.Code);

            if (matchInBank is not null)
            {
                var withdrawItem = new WithdrawItem(Character, gameState, item.Code, 1);

                withdrawItem.onAfterSuccessEndHook = async () =>
                {
                    await Character.SmartItemEquip(item.Code, 1);
                };

                return withdrawItem;
            }

            var otherBagsInInventory = Character.GetItemsFromInventoryWithType("bag");

            foreach (var inventoryBag in otherBagsInInventory)
            {
                if (!ItemService.CanUseItem(inventoryBag.Item, Character.Schema))
                {
                    continue;
                }

                if (inventoryBag.Item.Level > equippedBag?.Level)
                {
                    await Character.EquipItem(inventoryBag.Item.Code, "bag", 1);
                    return null;
                }
            }

            if (!await Character.PlayerActionService.CanObtainItem(item))
            {
                continue;
            }

            var job = new ObtainOrFindItem(Character, gameState, item.Code, 1);

            job.onAfterSuccessEndHook = async () =>
            {
                await Character.SmartItemEquip(item.Code, 1);
            };

            return job;
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

    async Task<CharacterJob?> GetRoleJob()
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
                        bool canTrainWepCrafting = await TrainSkill.CanDoJob(
                            Character,
                            gameState,
                            Skill.Weaponcrafting
                        );

                        if (canTrainWepCrafting)
                        {
                            return new TrainSkill(
                                Character,
                                gameState,
                                Skill.Weaponcrafting,
                                1,
                                true
                            );
                        }
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
                        bool canTrainGearCrafting = await TrainSkill.CanDoJob(
                            Character,
                            gameState,
                            Skill.Gearcrafting
                        );

                        if (canTrainGearCrafting)
                        {
                            return new TrainSkill(
                                Character,
                                gameState,
                                Skill.Gearcrafting,
                                1,
                                true
                            );
                        }
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
                        bool canTrainJewelryCrafting = await TrainSkill.CanDoJob(
                            Character,
                            gameState,
                            Skill.Jewelrycrafting
                        );

                        if (canTrainJewelryCrafting)
                        {
                            return new TrainSkill(
                                Character,
                                gameState,
                                Skill.Jewelrycrafting,
                                1,
                                true
                            );
                        }
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
            if (await Character.PlayerActionService.CanItemFromItemTaskBeObtained())
            {
                return new ItemTask(Character, gameState);
            }
            else
            {
                if (await CancelTask.CanCancelTask(Character, gameState))
                {
                    return new CancelTask(Character, gameState);
                }
            }
        }
        else if (Character.Schema.TaskType == TaskType.monsters.ToString())
        {
            var nextJobResult = await GetNextJobToFightMonster(
                gameState.AvailableMonstersDict.GetValueOrNull(Character.Schema.Task)!
            );

            if (nextJobResult is not null)
            {
                if (nextJobResult.Job is not null)
                {
                    var nextJob = nextJobResult.Job;

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
                    $"{Name}: [{Character.Schema.Name}]: GetIndividualLowPrioJob: Finding a train combat job - fighting {fightMonster.Amount} x {fightMonster.Code}"
                );
                var nextJobResult = await GetNextJobToFightMonster(
                    gameState.AvailableMonstersDict.GetValueOrNull(fightMonster.Code)!
                );

                if (nextJobResult is not null)
                {
                    if (nextJobResult.Job is not null)
                    {
                        var nextJob = nextJobResult.Job;

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

            var matchingMonster = gameState.AvailableMonstersDict.GetValueOrNull(task.Code)!;

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
            var monster = gameState.AvailableMonstersDict.GetValueOrNull(Character.Schema.Task)!;
            var nextJobResult = await GetNextJobToFightMonster(monster);

            if (nextJobResult is not null)
            {
                if (nextJobResult.Job is not null)
                {
                    logger.LogInformation(
                        $"{Name}: [{Character.Schema.Name}]: GetIndividualHighPrioJob: Job found - do monster task ({monster.Code})"
                    );

                    var nextJob = nextJobResult.Job;

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
        var matchingMonster = gameState.AvailableMonstersDict.GetValueOrNull(eventContent.Code);

        if (matchingMonster is not null && matchingMonster.Level <= Character.Schema.Level)
        {
            var jobsToFightMonster = await GetNextJobToFightMonster(matchingMonster);

            if (jobsToFightMonster?.Job is not null)
            {
                var nextJob = jobsToFightMonster.Job;

                logger.LogInformation(
                    $"{Name}: [{Character.Schema.Name}]: GetEventJob: Doing first job to fight event monster - job is {nextJob.JobName} for {nextJob.Amount} x {nextJob.Code}"
                );
                return nextJob;
            }
            else if (jobsToFightMonster is not null && jobsToFightMonster.Job is null)
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

        return null;
    }

    async Task<CharacterJob?> GetNpcEventJob(MapContentSchema eventContent)
    {
        /**
        ** TODO: Simple handling - we know that the nomadic merchant sells artifacts, so we want to trigger the artifacts logic,
        ** when he is active
        */

        if (eventContent.Code == "nomadic_merchant")
        {
            return await EnsureAccessories();
        }

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

    async Task<NextJobToFightResult?> GetNextJobToFightMonster(MonsterSchema monster)
    {
        var jobsToGetItems = await Character.PlayerActionService.GetJobsToGetItemsToFightMonster(
            Character,
            gameState,
            monster
        );

        // Return null if they shouldn't fight, return list of jobs if they should, return empty NextJobToFightResult
        if (jobsToGetItems is null)
        {
            return null;
        }

        if (jobsToGetItems.Count == 0)
        {
            return new NextJobToFightResult { Job = null };
        }

        var bankItems = await gameState.BankItemCache.GetBankItems(Character);

        // We assume that items that are lower level, are also easier to get (mobs less difficult to fight).
        // The issue can be that our character might only barely be able to fight the monster, so rather get the easier items first
        jobsToGetItems.Sort(
            (a, b) =>
            {
                bool aIsInBank = bankItems.Data.Exists(item =>
                    item.Code == a.Job.Code && item.Quantity >= a.Job.Amount
                );

                bool bIsInBank = bankItems.Data.Exists(item =>
                    item.Code == b.Job.Code && item.Quantity >= b.Job.Amount
                );

                if (aIsInBank && !bIsInBank)
                {
                    return -1;
                }
                else if (bIsInBank && !aIsInBank)
                {
                    return 1;
                }

                // If we can buy an item straight away, then let us do that first
                var aMatchingNpcItem = gameState.NpcItemsDict.ContainsKey(a.Job.Code);

                var bMatchingNpcItem = gameState.NpcItemsDict.ContainsKey(b.Job.Code);

                if (aMatchingNpcItem && !bMatchingNpcItem)
                {
                    return -1;
                }
                else if (!aMatchingNpcItem && bMatchingNpcItem)
                {
                    return 1;
                }

                var aLevel = gameState.ItemsDict.GetValueOrNull(a.Job.Code)!.Level;
                var bLevel = gameState.ItemsDict.GetValueOrNull(b.Job.Code)!.Level;

                return aLevel.CompareTo(bLevel);
            }
        );

        var nextJob = jobsToGetItems.ElementAtOrDefault(0);

        if (nextJob is not null)
        {
            nextJob.Job.onAfterSuccessEndHook = async () =>
            {
                logger.LogInformation(
                    $"{Name}: [{Character.Name}]: onAfterSuccessEndHook: Equipping {nextJob.Job.Amount} x {nextJob.Job.Code}"
                );
                // TODO: In general, we should figure out how we handle rings/artifacts - how do we really know which item to replace? By level?
                await Character.EquipItem(
                    nextJob.Job.Code,
                    nextJob.Slot.Slot.FromPascalToSnakeCase(),
                    nextJob.Job.Amount
                );
            };
        }

        return new NextJobToFightResult { Job = nextJob?.Job };
    }

    public async Task<CharacterJob?> GetChoreJob()
    {
        logger.LogInformation($"{Name}: [{Character.Schema.Name}]: Evaluating chore jobs");

        foreach (var chore in Character.Chores)
        {
            if (gameState.ChoreService.ShouldChoreBeStarted(chore))
            {
                CharacterJob? job = null;
                bool isScheduledChore = false;

                switch (chore)
                {
                    case CharacterChoreKind.RecycleUnusedItems:
                        isScheduledChore = true;
                        job = await ProcessChoreJob(
                            new RecycleUnusedItems(Character, gameState),
                            CharacterChoreKind.RecycleUnusedItems
                        );
                        break;
                    case CharacterChoreKind.SellUnusedItems:
                        isScheduledChore = true;
                        job = await ProcessChoreJob(
                            new SellUnusedItems(Character, gameState),
                            CharacterChoreKind.SellUnusedItems
                        );
                        break;
                    case CharacterChoreKind.RestockFood:
                        job = await ProcessChoreJob(
                            new RestockFood(Character, gameState),
                            CharacterChoreKind.RestockFood
                        );
                        break;
                    case CharacterChoreKind.RestockTasksCoins:
                        job = await ProcessChoreJob(
                            new RestockTasksCoins(Character, gameState),
                            CharacterChoreKind.RestockTasksCoins
                        );
                        break;
                    case CharacterChoreKind.RestockPotions:
                        job = await ProcessChoreJob(
                            new RestockPotions(Character, gameState),
                            CharacterChoreKind.RestockPotions
                        );
                        break;
                    case CharacterChoreKind.RestockResources:
                        job = await ProcessChoreJob(
                            new RestockResources(Character, gameState),
                            CharacterChoreKind.RestockResources
                        );
                        break;
                    default:
                        return null;
                }

                if (job is null)
                {
                    return null;
                }

                logger.LogInformation(
                    $"{Name}: [{Character.Schema.Name}]: Assigning chore job \"{chore.GetDisplayName()}\""
                );

                // If it's not a scheduled chore, we don't keep track. We want to do this chore whenever it's needed.
                if (isScheduledChore)
                {
                    job.onAfterSuccessEndHook = async () =>
                    {
                        logger.LogInformation(
                            $"{Name}: [{Character.Name}]: Done running chore \"{chore.GetDisplayName()}\""
                        );
                        gameState.ChoreService.FinishChore(chore);
                    };

                    gameState.ChoreService.StartChore(Character, chore);
                }

                return job;
            }
        }

        return null;
    }

    public async Task<CharacterJob?> ProcessChoreJob<T>(T job, CharacterChoreKind chore)
        where T : CharacterJob, ICharacterChoreJob
    {
        if (!await job.NeedsToBeDone())
        {
            return null;
        }

        logger.LogInformation(
            $"{Name}: [{Character.Schema.Name}]: Assigning chore job \"{chore.GetDisplayName()}\""
        );

        return job;
    }
}

record NextJobToFightResult
{
    public required CharacterJob? Job;
}
