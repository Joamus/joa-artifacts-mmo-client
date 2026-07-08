using System.Text.Json.Serialization;
using Application.Artifacts.Schemas;
using Application.ArtifactsApi.Schemas;
using Application.ArtifactsApi.Schemas.Requests;
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
    private const int PERSONAL_GOLD_THRESHOLD = 10_000;

    private const int CHORE_LEVEL_OFFSET = 12;

    private const int QUANTIY_OF_EACH_TELEPORT_POTION = 1;

    private const bool PREFER_MONSTER_TASK = true;
    public PlayerCharacter Character { get; init; }

    public bool Enabled { get; set; } = true;

    private GameState gameState { get; set; }

    bool hasDoneItemTask { get; set; } = false;

    [JsonIgnore]
    public ILogger<CharacterJob> Logger { get; init; } =
        AppLogger.loggerFactory.CreateLogger<CharacterJob>();

    public PlayerAI(PlayerCharacter character, GameState gameState, bool enabled = true)
    {
        Character = character;
        this.gameState = gameState;
        Enabled = enabled;
    }

    public async Task<CharacterJob?> GetNextJob()
    {
        Logger.LogInformation(
            "{Name}: [{CharacterName}]: Evaluating next job",
            Name,
            Character.Schema.Name
        );

        hasDoneItemTask =
            gameState.AccountAchievements.FirstOrDefault(achiev =>
                achiev.Code == "tasks_farmer" && achiev.CompletedAt is not null
            )
                is not null;

        // Claim all the items that you can
        if (gameState.ShouldClaimPendingItems())
        {
            gameState.PendingItemClaimEvaluation = DateTime.UtcNow;
            await ClaimPendingItems();
        }

        // Deposit all gold above threshold - shared economy

        var job =
            await WithdrawAllowance()
            ?? DepositUnneededGold()
            ?? await WithdrawTeleportPotions()
            ?? await EnsureAccessories()
            ?? await EnsureWeapon()
            ?? await EnsureTools()
            ?? await GetEventJob()
            // Support characters should have the chores higher up in their prio list
            ?? (Character.CharacterConfig.SupportRole ? await GetChoreJob() : null)
            ?? await GetIndividualHighPrioJob()
            ?? await EnsureFightEquipment()
            ?? await EnsureBag()
            ?? GetSkillJob()
            ?? await GetRoleJob()
            ?? await GetChoreJob()
            ?? await GetIndividualLowPrioJob();

        Logger.LogInformation(
            $"{Name}: [{Character.Schema.Name}]: Found job - {job?.JobName} - code {job?.Code} x {job?.Amount}"
        );

        return job;
    }

    async Task<CharacterJob?> WithdrawTeleportPotions()
    {
        var bankItems = await gameState.BankItemCache.GetBankItems(Character);

        foreach (var item in bankItems)
        {
            if (string.IsNullOrWhiteSpace(item.Code))
            {
                continue;
            }

            var matchingItem = gameState.ItemsDict[item.Code];

            if (!ItemService.IsTeleportPotion(matchingItem))
            {
                continue;
            }

            int quantityInInventory = Character.GetItemFromInventory(item.Code)?.Quantity ?? 0;

            if (quantityInInventory >= QUANTIY_OF_EACH_TELEPORT_POTION)
            {
                continue;
            }

            int amountNeeded = QUANTIY_OF_EACH_TELEPORT_POTION - quantityInInventory;

            int amountToWithdraw = Math.Min(amountNeeded, item.Quantity);

            if (amountToWithdraw > 0)
            {
                return new WithdrawItem(Character, gameState, item.Code, amountToWithdraw, false);
            }
        }

        return null;
    }

    async Task ClaimPendingItems()
    {
        Logger.LogInformation(
            "{Name}: [{Character.Schema.Name}]: Claiming pending items",
            Name,
            Character.Schema.Name
        );

        bool canClaimItems = true;

        while (canClaimItems)
        {
            var pendingItems = await gameState.GetPendingItems();

            var itemWeCanClaim = pendingItems.FirstOrDefault(pendingItem =>
            {
                int slotsNeeded = 0;
                int spaceNeeded = 0;

                foreach (var item in pendingItem.Items)
                {
                    slotsNeeded++;
                    spaceNeeded += item.Quantity;
                }

                return Character.GetAvailableInventorySpace() > spaceNeeded
                    && Character.GetAvailableInventorySlots() >= slotsNeeded;
            });

            if (itemWeCanClaim is not null)
            {
                Logger.LogInformation(
                    "{Name}: [{Character.Schema.Name}]: Can claim item {itemWeCanClaim.Id} - description: {itemWeCanClaim.Description}",
                    Name,
                    Character.Schema.Name,
                    itemWeCanClaim.Id,
                    itemWeCanClaim.Description
                );
                await Character.ClaimPendingItem(itemWeCanClaim.Id);
            }
            else
            {
                canClaimItems = false;
            }
        }

        Logger.LogInformation(
            "{Name}: [{Character.Schema.Name}]: Claiming pending items - done",
            Name,
            Character.Schema.Name
        );
    }

    DepositGold? DepositUnneededGold()
    {
        if (Character.Schema.Gold > PERSONAL_GOLD_THRESHOLD)
        {
            int goldAboveThreshold = Character.Schema.Gold - PERSONAL_GOLD_THRESHOLD;

            Logger.LogInformation(
                $"{Name}: [{Character.Schema.Name}]: Depositing unneeded gold - depositing ({goldAboveThreshold})"
            );

            return new DepositGold(Character, gameState, goldAboveThreshold);
        }

        return null;
    }

    async Task<WithdrawGold?> WithdrawAllowance()
    {
        if (Character.Schema.Gold < PERSONAL_GOLD_THRESHOLD)
        {
            int budgetInBank = await Character.GetAllowedWithdrawAmount();

            int amountNeeded = PERSONAL_GOLD_THRESHOLD - Character.Schema.Gold;

            int amountToWithdraw = Math.Min(
                PERSONAL_GOLD_THRESHOLD,
                budgetInBank >= amountNeeded ? amountNeeded : budgetInBank
            );

            if (amountToWithdraw <= 0)
            {
                return null;
            }

            Logger.LogInformation(
                $"{Name}: [{Character.Schema.Name}]: Withdraw allowance - withdrawing {amountToWithdraw}"
            );

            return new WithdrawGold(Character, gameState, amountToWithdraw);
        }

        return null;
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
        List<ItemSchema> nonCombatArtifacts =
        [
            .. gameState.Items.Where(item =>
                // Don't really care for the seasonal stuff at the moment, but maybe we should
                item.Type == "artifact"
                && ItemService.CanUseItem(item, Character.Schema)
                && !item.Name.Contains("Christmas")
            ),
        ];

        if (nonCombatArtifacts.Count == 0)
        {
            return null;
        }

        List<(string ItemCode, string Slot)> slots =
        [
            (Character.Schema.Artifact1Slot, "artifact1"),
            (Character.Schema.Artifact2Slot, "artifact2"),
            (Character.Schema.Artifact3Slot, "artifact3"),
        ];

        var bankItems = await gameState.BankItemCache.GetBankItems(Character);

        var bankItemsDict = bankItems.ToDictionary((item) => item.Code);

        foreach (var (ItemCode, Slot) in slots)
        {
            // For now, only bother getting artifacts for empty slots. Later on, most artifacts are combat related anyway, so we will automatically find better artifacts then.
            if (!string.IsNullOrWhiteSpace(ItemCode))
            {
                continue;
            }

            foreach (var artifact in nonCombatArtifacts)
            {
                var result = Character.GetEquippedItemOrInInventory(artifact.Code);

                (EquipmentSlot inventorySlot, bool isEquipped)? itemInInventory =
                    result.Count > 0 ? result.ElementAt(0)! : null;

                if (itemInInventory is not null && !itemInInventory.Value.isEquipped)
                {
                    await Character.EquipItem(
                        new EquipRequest
                        {
                            Code = itemInInventory.Value.inventorySlot.Code,
                            Slot = Slot,
                            Quantity = 1,
                        }
                    );
                    continue;
                }

                var matchInBank = bankItemsDict.GetValueOrNull(artifact.Code);

                if (matchInBank is not null)
                {
                    var withdrawItem = new WithdrawItem(Character, gameState, artifact.Code, 1)
                    {
                        onAfterSuccessEndHook = async () =>
                        {
                            await Character.EquipItem(
                                new EquipRequest
                                {
                                    Code = artifact.Code,
                                    Slot = Slot,
                                    Quantity = 1,
                                }
                            );
                        },
                    };

                    return withdrawItem;
                }

                if (!await Character.PlayerActionService.CanObtainItem(artifact))
                {
                    continue;
                }

                /**
                ** TODO: This is very rudimentary artifact logic - we for now just want unique non-combat artifacts,
                ** hoping that it will give us well-balanced characters. In the future, we should optimize for having
                ** the best possible non-combat artifacts for specific jobs.
                */
                if (slots.Exists(slot => slot.ItemCode == artifact.Code))
                {
                    continue;
                }

                var job = new ObtainOrFindItem(Character, gameState, artifact.Code, 1)
                {
                    onAfterSuccessEndHook = async () =>
                    {
                        await Character.EquipItem(
                            new EquipRequest
                            {
                                Code = artifact.Code,
                                Slot = Slot,
                                Quantity = 1,
                            }
                        );
                    },
                };

                return job;
            }
        }

        return null;
    }

    async Task<CharacterJob?> EnsureFightEquipment()
    {
        /**
        * We just want to ensure some minimum level of fight equipment.
        * Characters that mostly do chores are often affected by having the minimum viable fight equipment,
        * which slows down things like fighting slimes for potions, etc.
        */

        Logger.LogInformation($"{Name}: [{Character.Schema.Name}]: Ensure fight equipment");

        var job = await FightEquipmentAI.EnsureFightEquipment(Character, gameState);

        Logger.LogInformation(
            $"{Name}: [{Character.Schema.Name}]: Ensure fight equipment - found job {job?.Code ?? "(none)"}"
        );

        return job;
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

        var bankItemsDict = bankItems.ToDictionary((item) => item.Code);

        foreach (var item in bagItems)
        {
            if (!ItemService.CanUseItem(item, Character.Schema))
            {
                continue;
            }

            var result = Character.GetEquippedItemOrInInventory(item.Code);

            (EquipmentSlot inventorySlot, bool isEquipped)? itemInInventory =
                result.Count > 0 ? result.ElementAt(0)! : null;

            if (itemInInventory is not null)
            {
                if (!itemInInventory.Value.isEquipped)
                {
                    await Character.EquipItem(
                        new EquipRequest
                        {
                            Code = itemInInventory.Value.inventorySlot.Code,
                            Slot = "bag",
                            Quantity = 1,
                        }
                    );
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
                var withdrawItem = new WithdrawItem(Character, gameState, item.Code, 1)
                {
                    onAfterSuccessEndHook = async () =>
                    {
                        await Character.SmartItemEquip(item.Code, 1);
                    },
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
                    await Character.EquipItem(
                        new EquipRequest
                        {
                            Code = inventoryBag.Item.Code,
                            Slot = "bag",
                            Quantity = 1,
                        }
                    );
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

        foreach (var item in bankItems)
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
            Logger.LogInformation(
                $"{Name}: [{Character.Schema.Name}]: Current weapon was {currentWeapon?.Code ?? "n/a"} - withdrawing 1 x {bestCandidate.Code}"
            );
            return new WithdrawItem(Character, gameState, bestCandidate.Code, 1, false);
        }

        Logger.LogInformation(
            $"{Name}: [{Character.Schema.Name}]: Could not find weapon from bank - cannot handle at the moment"
        );

        return null;
    }

    async Task<CharacterJob?> EnsureTools()
    {
        Logger.LogInformation($"{Name}: [{Character.Schema.Name}]: EnsureTools: Start");

        var bestTools = await ItemService.GetBestTools(Character, gameState, null, hasDoneItemTask);

        if (!hasDoneItemTask)
        {
            Logger.LogInformation(
                $"{Name}: [{Character.Schema.Name}]: EnsureTools: Tasks farmer achievement is not completed yet - evaluating best tools, which don't require task materials"
            );
        }

        ItemSchema? equippedWeapon = gameState.ItemsDict.GetValueOrNull(
            Character.Schema.WeaponSlot
        );

        foreach (var tool in bestTools)
        {
            var result = Character.GetEquippedItemOrInInventory(tool.Code);

            (EquipmentSlot inventorySlot, bool isEquipped)? itemInInventory =
                result.Count > 0 ? result.ElementAt(0)! : null;

            if (itemInInventory is not null)
            {
                continue;
            }

            if (Character.ExistsInWishlist(tool.Code))
            {
                Logger.LogInformation(
                    $"{Name}: [{Character.Schema.Name}]: EnsureTools: Skipping obtaining tool {tool.Code} - is already in wish list"
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

            Logger.LogInformation(
                $"{Name}: [{Character.Schema.Name}]: EnsureTools: Job found - get {itemCode}"
            );

            int itemAmount = 1;

            return new ObtainOrFindItem(Character, gameState, tool.Code, itemAmount);
        }

        Logger.LogInformation(
            "{Name}: [{Character.Schema.Name}]: EnsureTools: Ended - found no job",
            Name,
            Character.Schema.Name
        );
        return null;
    }

    async Task<CharacterJob?> GetIndividualHighPrioJob()
    {
        Logger.LogInformation(
            $"{Name}: [{Character.Schema.Name}]: GetIndividualHighPrioJob: Start"
        );

        // Highest prio is completing this achievement, else all task items are locked.
        if (!hasDoneItemTask)
        {
            return await GetTaskJobIfPossible(PREFER_MONSTER_TASK);
        }

        return null;
    }

    CharacterJob? GetSkillJob()
    {
        // if (Character.Schema.FishingLevel + SKILL_LEVEL_OFFSET <= Character.Schema.Level)
        // {
        //     logger.LogInformation(
        //         $"{Name}: [{Character.Schema.Name}]: GetSkillJob: Training Fishing - current level is {Character.Schema.FishingLevel}, compared to character level {Character.Schema.Level}"
        //     );
        //     return new TrainSkill(Character, gameState, Skill.Fishing, 1, true);
        // }

        // if (Character.Schema.CookingLevel + SKILL_LEVEL_OFFSET <= Character.Schema.Level)
        // {
        //     logger.LogInformation(
        //         $"{Name}: [{Character.Schema.Name}]: GetSkillJob: Training Cooking - current level is {Character.Schema.CookingLevel}, compared to character level {Character.Schema.Level}"
        //     );
        //     return new TrainSkill(Character, gameState, Skill.Cooking, 1, true);
        // }

        if (Character.Schema.AlchemyLevel + SKILL_LEVEL_OFFSET <= Character.Schema.Level)
        {
            Logger.LogInformation(
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
            var job = role switch
            {
                Skill.Weaponcrafting => await GetCraftingTrainingJob(
                    Skill.Weaponcrafting,
                    Character.Schema.WeaponcraftingLevel
                ),
                Skill.Gearcrafting => await GetCraftingTrainingJob(
                    Skill.Gearcrafting,
                    Character.Schema.GearcraftingLevel
                ),
                Skill.Jewelrycrafting => await GetCraftingTrainingJob(
                    Skill.Jewelrycrafting,
                    Character.Schema.JewelrycraftingLevel
                ),
                _ => null,
            };

            if (job is not null)
            {
                return job;
            }
        }

        return null;
    }

    async Task<CharacterJob> GetIndividualLowPrioJob()
    {
        bool hasNoTask = string.IsNullOrWhiteSpace(Character.Schema.Task);

        // var newTask = await GetTaskJob(PREFER_MONSTER_TASK);

        // if (newTask is not null)
        // {
        //     return newTask;
        // }

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
                Logger.LogInformation(
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

                        Logger.LogInformation(
                            $"{Name}: [{Character.Schema.Name}]: GetIndividualLowPrioJob: Doing first job to fight {fightMonster.Amount} x {fightMonster.Code} - job is {nextJob.JobName} for {nextJob.Amount} x {nextJob.Code}"
                        );
                        // Do the first job in the list, we only do one thing at a time
                        return nextJob;
                    }
                    else
                    {
                        Logger.LogInformation(
                            $"{Name}: [{Character.Schema.Name}]: GetIndividualLowPrioJob: Fighting {fightMonster.Amount} x {fightMonster.Code}"
                        );
                        return fightMonster;
                    }
                }
            }
        }

        Logger.LogInformation(
            $"{Name}: [{Character.Schema.Name}]: GetIndividualLowPrioJob: Fallback job"
        );

        if (hasNoTask)
        {
            Logger.LogInformation(
                $"{Name}: [{Character.Schema.Name}]: GetIndividualLowPrioJob: Got no task - take item task"
            );

            return new AcceptNewTask(Character, gameState, TaskType.items);
        }

        Logger.LogInformation(
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

    async Task<CharacterJob?> GetTaskJobIfPossible(bool preferMonsterTask)
    {
        Logger.LogInformation(
            "{Name}: [{Character.Schema.Name}]: GetTaskJob: Start",
            Name,
            Character.Schema.Name
        );

        if (Character.Schema.TaskType == TaskType.monsters.ToString())
        {
            var monster = gameState.AvailableMonstersDict.GetValueOrNull(Character.Schema.Task)!;
            var nextJobResult = await GetNextJobToFightMonster(monster);

            if (nextJobResult is not null)
            {
                if (nextJobResult.Job is not null)
                {
                    Logger.LogInformation(
                        $"{Name}: [{Character.Schema.Name}]: GetTaskJob: Job found - do monster task ({monster.Code})"
                    );

                    var nextJob = nextJobResult.Job;

                    Logger.LogInformation(
                        $"{Name}: [{Character.Schema.Name}]: GetTaskJob: Doing first job to fight job for monster task - fighting {Character.Schema.TaskTotal - Character.Schema.TaskProgress} x {monster.Code} - job is {nextJob.JobName} for {nextJob.Amount} x {nextJob.Code}"
                    );
                    // Do the first job in the list, we only do one thing at a time
                    return nextJob;
                }
                else
                {
                    Logger.LogInformation(
                        $"{Name}: [{Character.Schema.Name}]: GetTaskJob: No items left to get to do monster task - fighting {Character.Schema.TaskTotal - Character.Schema.TaskProgress} x {monster.Code}"
                    );
                    return new MonsterTask(Character, gameState);
                }
            }
        }
        else if (Character.Schema.TaskType == TaskType.monsters.ToString())
        {
            if (await Character.PlayerActionService.CanItemFromItemTaskBeObtained())
            {
                Logger.LogInformation(
                    $"{Name}: [{Character.Schema.Name}]: GetTaskJob: Found new item task"
                );
                return new ItemTask(Character, gameState);
            }
            else if (await CancelTaskJob.CanCancelTask(Character, gameState))
            {
                await CancelTaskJob.DoCancelTask(Character, gameState);
            }
            else
            {
                return null;
            }
        }

        if (preferMonsterTask && CanHandlePotentialMonsterTasks())
        {
            Logger.LogInformation(
                "{Name}: [{Character.Schema.Name}]: GetTaskJob: Found new monster task",
                Name,
                Character.Schema.Name
            );

            return new MonsterTask(Character, gameState);
        }
        if (await Character.PlayerActionService.CanItemFromItemTaskBeObtained())
        {
            Logger.LogInformation(
                $"{Name}: [{Character.Schema.Name}]: GetTaskJob: Found new item task"
            );
            return new ItemTask(Character, gameState);
        }

        Logger.LogInformation($"{Name}: [{Character.Schema.Name}]: GetTaskJob: No job found");

        return null;
    }

    async Task<CharacterJob?> GetEventJob()
    {
        var activeEvents = gameState.EventService.ActiveEvents;

        Logger.LogInformation(
            $"{Name}: [{Character.Schema.Name}]: GetEventJob: Evaluating active events - there are {activeEvents.Count} active events"
        );

        if (activeEvents.Count == 0)
        {
            Logger.LogInformation(
                $"{Name}: [{Character.Schema.Name}]: GetEventJob: Evaluating active events - found no events"
            );
            return null;
        }

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
                Logger.LogInformation(
                    "{Name}: [{Character.Schema.Name}]: GetEventJob: Found event job - code: {code}",
                    Name,
                    Character.Schema.Name,
                    job.Code
                );
                return job;
            }
        }

        Logger.LogInformation(
            $"{Name}: [{Character.Schema.Name}]: GetEventJob: No event job scheduled out of {activeEvents.Count} active events"
        );

        return null;
    }

    public async Task EvaluateEventsChanged()
    {
        var nextJob = await GetNextJob();

        if (nextJob is null)
        {
            return;
        }

        Logger.LogInformation(
            $"{Character.Name} - events changed, found {nextJob.Code} x {nextJob?.Amount} job for them"
        );

        if (
            nextJob is not null
            && !JobsHaveOverlap(nextJob, Character.CurrentJob)
            && !Character.Jobs.Exists(job => JobsHaveOverlap(job, nextJob))
        )
        {
            Logger.LogInformation(
                $"{Character.Name} - assigning job \"{nextJob.Code}\" - clearing job queue, scheduling this job as highest priority"
            );
            Character.ClearJobs();
            await Character.QueueJob(nextJob, true);
        }
    }

    async Task<CharacterJob?> GetMonsterEventJob(MapContentSchema eventContent)
    {
        var matchingMonster = gameState.AvailableMonstersDict.GetValueOrNull(eventContent.Code);

        if (
            matchingMonster is not null
            && matchingMonster.Level <= Character.Schema.Level
            && matchingMonster.Type != MonsterType.Boss
        )
        {
            var jobsToFightMonster = await GetNextJobToFightMonster(matchingMonster);

            if (jobsToFightMonster?.Job is not null)
            {
                var nextJob = jobsToFightMonster.Job;

                Logger.LogInformation(
                    $"{Name}: [{Character.Schema.Name}]: GetEventJob: Doing first job to fight event monster - job is {nextJob.JobName} for {nextJob.Amount} x {nextJob.Code}"
                );
                return nextJob;
            }
            else if (jobsToFightMonster is not null && jobsToFightMonster.Job is null)
            {
                Logger.LogInformation(
                    $"{Name}: [{Character.Schema.Name}]: GetMonsterEventjob: No items left to get to do fight event monster - fighting {TrainCombat.AMOUNT_TO_KILL} x {matchingMonster.Code}"
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

        /**
        ** This is a bit of a hack - if an NPC is now available, e.g. fish_merchant, we might have items to sell to them.
        ** We can expand upon this, to also restock items that we can buy from them, e.g. algae, if we make a job for that (or change RestockResources)
        */
        if (Character.Chores.Exists(chore => chore == CharacterChoreKind.SellUnusedItems))
        {
            var job = new SellUnusedItems(Character, gameState);

            if (await job.NeedsToBeDone())
            {
                return job;
            }
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
                bool aIsInBank = bankItems.Exists(item =>
                    item.Code == a.Job.Code && item.Quantity >= a.Job.Amount
                );

                bool bIsInBank = bankItems.Exists(item =>
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
                Logger.LogInformation(
                    $"{Name}: [{Character.Name}]: onAfterSuccessEndHook: Equipping {nextJob.Job.Amount} x {nextJob.Job.Code}"
                );
                // TODO: In general, we should figure out how we handle rings/artifacts - how do we really know which item to replace? By level?
                await Character.EquipItem(
                    new EquipRequest
                    {
                        Code = nextJob.Job.Code,
                        Slot = nextJob.Slot.Slot.FromPascalToSnakeCase(),
                        Quantity = nextJob.Job.Amount,
                    }
                );
            };
        }

        return new NextJobToFightResult { Job = nextJob?.Job };
    }

    public async Task<CharacterJob?> GetChoreJob()
    {
        Logger.LogInformation(
            "{Name}: [{Character.Schema.Name}]: Evaluating chore jobs",
            Name,
            Character.Schema.Name
        );

        var levelRange = GameState.GetCharacterLevelRange(gameState);

        if (levelRange.Highest >= Character.Schema.Level + CHORE_LEVEL_OFFSET)
        {
            Logger.LogInformation(
                "{Name}: [{Character.Schema.Name}]: Evaluating chore jobs - skipping chore jobs, because character is underlevelled",
                Name,
                Character.Schema.Name
            );

            return null;
        }

        List<ChorePriority> chorePriorities = [ChorePriority.High, ChorePriority.Low];

        foreach (var priority in chorePriorities)
        {
            if (!(Character.CharacterConfig?.SupportRole ?? false) && priority < ChorePriority.High)
            {
                continue;
            }

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
                                new RestockFood(Character, gameState, priority),
                                CharacterChoreKind.RestockFood
                            );
                            break;
                        case CharacterChoreKind.RestockTasksCoins:
                            job = await ProcessChoreJob(
                                new RestockTasksCoins(Character, gameState, priority),
                                CharacterChoreKind.RestockTasksCoins
                            );
                            break;
                        case CharacterChoreKind.RestockPotions:
                            job = await ProcessChoreJob(
                                new RestockPotions(Character, gameState, priority),
                                CharacterChoreKind.RestockPotions
                            );
                            break;
                        case CharacterChoreKind.RestockResources:
                            job = await ProcessChoreJob(
                                new RestockResources(Character, gameState, priority),
                                CharacterChoreKind.RestockResources
                            );
                            break;
                        case CharacterChoreKind.RestockEventRelatedItems:
                            job = await ProcessChoreJob(
                                new RestockEventRelatedItems(Character, gameState),
                                CharacterChoreKind.RestockEventRelatedItems
                            );
                            break;
                        default:
                            return null;
                    }

                    if (job is null)
                    {
                        continue;
                    }

                    Logger.LogInformation(
                        $"{Name}: [{Character.Schema.Name}]: Assigning chore job \"{chore.GetDisplayName()}\""
                    );

                    // If it's not a scheduled chore, we don't keep track. We want to do this chore whenever it's needed.
                    if (isScheduledChore)
                    {
                        job.onAfterSuccessEndHook = async () =>
                        {
                            Logger.LogInformation(
                                $"{Name}: [{Character.Name}]: Done running chore \"{chore.GetDisplayName()}\""
                            );
                            gameState.ChoreService.FinishChore(chore);
                        };

                        gameState.ChoreService.StartChore(Character, chore);
                    }

                    return job;
                }
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

        Logger.LogInformation(
            $"{Name}: [{Character.Schema.Name}]: Assigning chore job \"{chore.GetDisplayName()}\""
        );

        return job;
    }

    static bool JobsHaveOverlap(CharacterJob? a, CharacterJob? b)
    {
        if (a is null || b is null)
        {
            return false;
        }

        return a.Code == b.Code
            || a.ParentJob?.Code == b.Code
            || a.ParentJob?.Code == b.ParentJob?.Code
            || a.Code == b.ParentJob?.Code;
    }

    async Task<CharacterJob?> GetCraftingTrainingJob(Skill skill, int skillLevel)
    {
        if (skillLevel + SKILL_LEVEL_OFFSET <= Character.Schema.Level)
        {
            Logger.LogInformation(
                $"{Name}: [{Character.Schema.Name}]: GetRoleJob: Training {skill.GetDisplayName()} - current level is {skillLevel}, compared to character level {Character.Schema.Level}"
            );
            bool canTrainCraftingSkill = await TrainSkill.CanDoJob(Character, gameState, skill);

            if (canTrainCraftingSkill)
            {
                // return new TrainSkill(
                //     Character,
                //     gameState,
                //     skill,
                //     1,
                //     true
                // );

                /**
                ** We want to only craft one at a time, in case the crafter gets a new job assigned,
                ** e.g. other characters want to have an item crafted for them
                */
                //
                var itemToCraft = await TrainSkill.GetJobsRequired(
                    Character,
                    gameState,
                    skill,
                    SkillKind.Crafting,
                    skillLevel
                );

                return itemToCraft.Value switch
                {
                    List<CharacterJob> list => list.First(),
                    _ => null,
                };
            }
        }

        return null;
    }
}

record NextJobToFightResult
{
    public required CharacterJob? Job;
}
