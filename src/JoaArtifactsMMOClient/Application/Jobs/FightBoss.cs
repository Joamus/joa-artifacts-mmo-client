using Application.ArtifactsApi.Schemas;
using Application.ArtifactsApi.Schemas.Requests;
using Application.ArtifactsApi.Schemas.Responses;
using Application.Character;
using Application.Errors;
using Applicaton.Services.FightSimulator;
using Microsoft.OpenApi.Extensions;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public class FightBoss
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string JobName { get; private set; } = "FightBoss";

    private static ILogger Logger { get; set; } = AppLogger.loggerFactory.CreateLogger<FightBoss>();

    private readonly FifoSemaphore GetNextJobLock = new(1, 1);
    public const int CHARACTERS_IN_BOSS_FIGHT = 3;
    public const int WAIT_WHEN_NO_JOB_MS = 5_000;
    public FightBossStatus Status = FightBossStatus.New;

    public DateTime CreatedAt = DateTime.UtcNow;
    public DateTime? LastFight = null;

    public required PlayerCharacter MainCharacter { get; set; }
    public required MonsterSchema Monster { get; set; }

    public string? ItemCode { get; private set; }
    public JobMode Mode { get; private set; } = JobMode.Kill;

    protected int Amount { get; set; } = 0;
    protected int ProgressAmount { get; set; } = 0;
    protected int InitialAmount { get; set; } = 0;
    public required GameState GameState { get; set; }
    public required List<PlayerCharacter> OtherCharacters { get; set; }
    public required List<PlayerCharacter> AllCharacters { get; set; }
    public required List<BossFightCharacterStatus> AllCharactersStatuses { get; set; }

    public required List<FightSimResult> LastFightSimResult { get; set; }

    public static async Task<FightBoss?> InitializeFightBossJob(
        PlayerCharacter character,
        GameState gameState,
        List<PlayerCharacter> otherCharacters,
        MonsterSchema monster,
        string? itemCode,
        int amount
    )
    {
        Logger.LogInformation(
            $"FightBoss: [{character.Schema.Name}]: Initializing fight boss job to fight {monster.Code}"
        );

        var result = await GetLastFightSimAndRequirements(
            character,
            otherCharacters,
            gameState,
            monster
        );

        List<FightSimResult> fightSim = [];

        result.Switch(
            appError =>
            {
                throw appError;
            },
            simResult =>
            {
                fightSim = simResult;
            }
        );

        List<PlayerCharacter> allCharacters = [character, .. otherCharacters];

        List<BossFightCharacterStatus> allCharactersStatuses =
        [
            .. allCharacters.Select(character =>
            {
                return new BossFightCharacterStatus
                {
                    Character = character,
                    Status = CharacterFightBossStatus.New,
                };
            }),
        ];

        var mode = itemCode is null ? JobMode.Kill : JobMode.Gather;

        var job = new FightBoss
        {
            MainCharacter = character,
            GameState = gameState,
            OtherCharacters = otherCharacters,

            AllCharacters = allCharacters,

            LastFightSimResult = fightSim,

            AllCharactersStatuses = allCharactersStatuses,

            Monster = monster,
            Mode = mode,
            ItemCode = itemCode,
            Amount = amount,
            InitialAmount =
                mode == JobMode.Gather
                    ? character.GetItemFromInventory(itemCode!)?.Quantity ?? 0
                    : 0,
        };

        bool notAllJoined = false;

        List<PlayerCharacter> joinedParticipants = [];

        foreach (var jobParticipant in allCharacters)
        {
            bool didJoin = await jobParticipant.JoinBossFightJob(job);

            if (!didJoin)
            {
                notAllJoined = true;
                break;
            }
            else
            {
                joinedParticipants.Add(jobParticipant);
            }
        }

        if (notAllJoined)
        {
            foreach (var participant in allCharacters)
            {
                await participant.LeaveBossFightJob();
            }

            Logger.LogInformation(
                $"FightBoss: [{character.Schema.Name}]: Failed to initialize fight boss job to fight {monster.Code} - stopping"
            );
            return null;
        }

        Logger.LogInformation(
            $"FightBoss: [{character.Schema.Name}]: Initialized fight boss job to fight {monster.Code}"
        );
        return job;
    }

    void RemoveCharacterFromJob(PlayerCharacter character)
    {
        if (character.CurrentFightBossJob?.Id == Id)
        {
            character.CurrentFightBossJob = null;
        }
    }

    public void Disband(string reason, bool successful)
    {
        // Assume that a job is failed when we remove people
        Status = successful ? FightBossStatus.Completed : FightBossStatus.Failed;

        Logger.LogInformation(
            $"{JobName}: Disbanding group - status: {Status.GetDisplayName()} - reason {reason}"
        );

        foreach (var character in AllCharacters)
        {
            RemoveCharacterFromJob(character);
        }

        AllCharacters = [];
        AllCharactersStatuses = [];
        OtherCharacters = [];
    }

    void HandleError(string errorReason)
    {
        Disband(errorReason, false);
    }

    public async Task<OneOf<AppError, List<CharacterJob>>> GetNextJobs(PlayerCharacter character)
    {
        var result = await InnerGetNextJobs(character);

        if (result.IsT0)
        {
            return result.AsT0;
        }
        else
        {
            var jobs = result.AsT1;

            if (jobs.Count == 0 && Status != FightBossStatus.Failed)
            {
                // We do this outside of the lock, so other characters can get their next job
                await Task.Delay(WAIT_WHEN_NO_JOB_MS);
            }
            return jobs;
        }
    }

    async Task<OneOf<AppError, List<CharacterJob>>> InnerGetNextJobs(PlayerCharacter character)
    {
        List<CharacterJob> nextPreparationJobs = [];

        // Dirty hack, but we cannot just return [] without casting
        List<CharacterJob> emptyJobs = [];

        try
        {
            await GetNextJobLock.WaitAsync();

            Logger.LogInformation(
                $"{JobName}: [{character.Schema.Name}]: Getting next job(s) to fight {Monster.Code}.."
            );

            switch (Status)
            {
                case FightBossStatus.Fighting:
                    Logger.LogInformation(
                        $"{JobName}: [{character.Schema.Name}]: Status is fighting - returning (fighting {Monster.Code}..)"
                    );
                    return emptyJobs;
                case FightBossStatus.Failed:
                    return new AppError(
                        $"FightBoss job with for monster {Monster.Code} has failed/been disbaned - character {character.Name} cannot get a next job"
                    );
                case FightBossStatus.Completed:
                    return emptyJobs;
            }

            if (AreAllReadyToFightBoss())
            {
                Logger.LogInformation(
                    $"{JobName}: [{character.Schema.Name}]: Detected that the group is ready to fight {Monster.Code}.."
                );
                Status = FightBossStatus.Fighting;
                await StartBossFight();

                switch (Mode)
                {
                    case JobMode.Kill:
                        ProgressAmount += 1;
                        break;
                    case JobMode.Gather:
                        int amountInInventory =
                            MainCharacter.GetItemFromInventory(ItemCode!)?.Quantity ?? 0;

                        ProgressAmount = amountInInventory - InitialAmount;
                        break;
                }

                Logger.LogInformation(
                    $"{JobName}: [{character.Schema.Name}]: Fought {Monster.Code} - progress is {ProgressAmount}/{Amount}{(Mode == JobMode.Gather ? $" (gathering {ItemCode})" : $"")}"
                );

                if (ProgressAmount >= Amount)
                {
                    Logger.LogInformation(
                        $"{JobName}: [{character.Schema.Name}]: Done fighting {Monster.Code} - progress is {ProgressAmount}/{Amount}{(Mode == JobMode.Gather ? $" (gathering {ItemCode})" : $"")}"
                    );

                    Disband("Done with job", true);
                }
                /**
                ** Delay is here, inside of the lock, to prevent data races with characters being asked to do things, while they are on cooldown
                */

                await Task.Delay(WAIT_WHEN_NO_JOB_MS);
                /**
                ** We don't neccessarily need the return here, we could just fall through and give the next job,
                ** but it's just easier to understand that they are different flows
                */
                return emptyJobs;
            }

            bool isReadyToFight = false;

            if (GetCharacterStatus(character) == CharacterFightBossStatus.New)
            {
                var nextPreparationJobResult = await GetPreparationJob(character);

                if (nextPreparationJobResult.IsT0)
                {
                    return nextPreparationJobResult.AsT0;
                }

                nextPreparationJobs = nextPreparationJobResult.AsT1;

                // First time prompting for jobs, but there is nothing to get - then they are ready
                isReadyToFight = nextPreparationJobs.Count == 0;
            }
            else if (GetCharacterStatus(character) == CharacterFightBossStatus.Preparing)
            {
                Logger.LogInformation(
                    $"{JobName}: [{character.Schema.Name}]: Status is \"preparing\", so should be ready to fight - fighting {Monster.Code}"
                );
                isReadyToFight = true;
            }

            if (isReadyToFight)
            {
                if (GetCharacterStatus(character) == CharacterFightBossStatus.ReadyToFight)
                {
                    Logger.LogInformation(
                        $"{JobName}: [{character.Schema.Name}]: Is ready to fight, but waiting for others - fighting {Monster.Code}"
                    );
                }
                else
                {
                    Logger.LogInformation(
                        $"{JobName}: [{character.Schema.Name}]: Changing status - ready to fight {Monster.Code}"
                    );
                    SetCharacterStatus(character, CharacterFightBossStatus.ReadyToFight);
                    // Might as well do it here already, so they are ready to fight
                    _ = PreFightRoutineForCharacter(character);
                }
            }
        }
        catch (AppError appError)
        {
            Logger.LogError(
                $"{GetType().Name}: [{character.Name}] boss job cancelled (main character: {MainCharacter.Name}) for boss {Monster.Code} with appError: {appError.Message}"
            );
            HandleError($"AppError caught for {character.Name} - {appError.Message}");
            return appError;
        }
        catch (Exception e)
        {
            AppError appError = new(e.Message);
            Logger.LogError(
                $"{GetType().Name}: [{character.Name}] boss job cancelled (main character: {MainCharacter.Name}) for boss {Monster.Code} with generic exception: {appError.Message}"
            );

            HandleError($"Generic error caught for {character.Name} - {appError.Message}");
            return appError;
        }
        finally
        {
            GetNextJobLock.Release();
        }

        // Set this fight boss job as the parent collab job of all jobs we return.
        nextPreparationJobs.ForEach(job => job.ParentCollabJobId = Id);

        if (nextPreparationJobs.Count > 0)
        {
            /**
            ** The character's status should be "New" here, and after they have done these jobs, they should be ready in preparing.
            ** They reason that we have this Preparing state, is to prevent another character from just starting the fight, even though
            ** they haven't done the jobs they need to do yet.
            */
            SetCharacterStatus(character, CharacterFightBossStatus.Preparing);
        }

        return nextPreparationJobs;
    }

    bool AreAllReadyToFightBoss()
    {
        return AllCharactersStatuses.All(status =>
            status.Status == CharacterFightBossStatus.ReadyToFight
        );
    }

    void SetCharacterStatus(PlayerCharacter character, CharacterFightBossStatus status)
    {
        AllCharactersStatuses.First(status => status.Character.Name == character.Name).Status =
            status;
    }

    CharacterFightBossStatus GetCharacterStatus(PlayerCharacter character)
    {
        return AllCharactersStatuses
            .First(status => status.Character.Name == character.Name)
            .Status;
    }

    void ResetCharacterStatuses()
    {
        AllCharactersStatuses =
        [
            .. AllCharactersStatuses.Select(status =>
                status with
                {
                    Status = CharacterFightBossStatus.New,
                }
            ),
        ];
    }

    async Task StartBossFight()
    {
        LastFight = DateTime.UtcNow;

        Logger.LogInformation(
            $"{JobName}: StartBossFight: Running pre-fight routine for all participants - fighting {Monster.Code}"
        );

        var preFightRoutinesForAllCharacters = AllCharacters
            .Select(PreFightRoutineForCharacter)
            .ToList();

        await Task.WhenAll(preFightRoutinesForAllCharacters);

        Logger.LogInformation(
            $"{JobName}: StartBossFight: Pre-fight routines are done - fighting {Monster.Code}"
        );

        if (Status == FightBossStatus.Failed)
        {
            return;
        }

        await MainCharacter.Fight(OtherCharacters);

        Logger.LogInformation($"{JobName}: StartBossFight: Round against {Monster.Code} done");

        var fightSimResult = await GetLastFightSimAndRequirements(
            MainCharacter,
            OtherCharacters,
            GameState,
            Monster
        );

        // Ugly, but double guard so we can fail the job
        if (Status == FightBossStatus.Failed)
        {
            return;
        }

        fightSimResult.Switch(
            appError =>
            {
                Logger.LogInformation(
                    $"{JobName}: StartBossFight: Error - fight sim after fighting is no longer favorable - throwing error (fighting {Monster.Code})"
                );

                throw appError;
            },
            simResult =>
            {
                Logger.LogInformation(
                    $"{JobName}: StartBossFight: Fight sim is still favorable, proceeding"
                );

                // We don't want them to swap other items, only go back if they need new potions
                var simResultWithOnlyPotionsToEquip = simResult.Select(result =>
                {
                    var newItemsToEquip = result
                        .ItemsToEquip.Where(item =>
                        {
                            var matchingItem = GameState.ItemsDict[item.Code];

                            return matchingItem.Type != "utility";
                        })
                        .ToList();

                    var newResult = result with { ItemsToEquip = newItemsToEquip };

                    return newResult;
                });

                LastFightSimResult = simResult;
                ResetCharacterStatuses();
                Status = FightBossStatus.Preparing;
            }
        );
    }

    async Task PreFightRoutineForCharacter(PlayerCharacter character)
    {
        await character.WaitForCooldown();
        await FightMonster.HealIfNotAtFullHp(character, GameState, true);
        // await character.PlayerActionService.EquipBestFightEquipment(Monster);

        await character.NavigateTo(Monster.Code);
        await character.WaitForCooldown();
    }

    public static List<PlayerCharacter>? GetBestCandidatesToFight(
        PlayerCharacter character,
        GameState gameState
    )
    {
        List<PlayerCharacter> otherAvailablePlayers =
        [
            .. gameState
                .Characters.Where(otherCharacter =>
                    otherCharacter.Schema.Name != character.Schema.Name
                    && (otherCharacter.CurrentFightBossJob is null)
                )
                .OrderByDescending((b) => b.Schema.Level),
        ];

        // Allow partial groups, e.g. only 2, since there might be cases where they can win
        int amountToRecruit = CHARACTERS_IN_BOSS_FIGHT - 1;

        return otherAvailablePlayers.GetRange(0, amountToRecruit);
    }

    public static async Task<bool> CanFulfillRequirementsForFightingBoss(
        PlayerCharacter character,
        List<PlayerCharacter>? otherCharacters,
        GameState gameState,
        MonsterSchema monster
    )
    {
        if (otherCharacters is null)
        {
            var bestCandidates = GetBestCandidatesToFight(character, gameState);

            if (bestCandidates is null)
            {
                return false;
            }

            otherCharacters = bestCandidates;
        }

        var result = await GetLastFightSimAndRequirements(
            character,
            otherCharacters,
            gameState,
            monster
        );

        return result.Match(
            appError => false,
            fightSimResults => fightSimResults.All(result => result.Outcome.ShouldFight)
        );
    }

    public static async Task<OneOf<AppError, List<FightSimResult>>> GetLastFightSimAndRequirements(
        PlayerCharacter character,
        List<PlayerCharacter> otherCharacters,
        GameState gameState,
        MonsterSchema monster
    )
    {
        var bankItems = await gameState.Services.BankItemCache.GetBankItems(character);
        var bankDetails = await gameState.Services.BankItemCache.GetBankDetails();

        var result = FightSimulator.SimulateBossFightOutcome(
            character,
            otherCharacters,
            gameState,
            await gameState.Services.BankItemCache.GetBankItems(character),
            monster
        );

        if (result.All(simResult => !simResult.Outcome.ShouldFight))
        {
            return new AppError($"Should not fight boss {monster.Code}");
        }

        List<PlayerCharacter> allCharacters = [character, .. otherCharacters];

        var allReqItemsCanBeWithdrawn = await ValidateThatAllRequirementItemsCanBeWithdrawn(
            allCharacters,
            monster,
            bankDetails,
            bankItems
        );

        if (allReqItemsCanBeWithdrawn.IsT0)
        {
            return allReqItemsCanBeWithdrawn.AsT0;
        }

        return result;
    }

    public static async Task<OneOf<AppError, None>> ValidateThatAllRequirementItemsCanBeWithdrawn(
        List<PlayerCharacter> allCharacters,
        MonsterSchema monster,
        BankDetails bankDetails,
        List<DropSchema> bankItems
    )
    {
        var mutatingBankItems = bankItems.Select(item => item with { }).ToList();
        var mutatingBankDetails = bankDetails with { };

        foreach (var character in allCharacters)
        {
            var jobsNeededForNavigationResult =
                await character.PlayerActionService.NavigationService.GetJobsNeededForNavigation(
                    monster.Code
                );

            if (jobsNeededForNavigationResult.IsT0)
            {
                return jobsNeededForNavigationResult.AsT0;
            }

            var requirementsResult = GetRequirementsJobIfTheyCanBeWithdrawn(
                bankDetails,
                bankItems,
                jobsNeededForNavigationResult.AsT1
            );

            if (requirementsResult.Value is AppError)
            {
                return requirementsResult.AsT0;
            }

            var requirements = requirementsResult.AsT1;

            mutatingBankDetails.Gold -= requirements.GoldToWithdraw;

            requirements.ItemsToWithdraw.ForEach(itemToWithdraw =>
            {
                var matchInBank = mutatingBankItems.First(bankItem =>
                    bankItem.Code == itemToWithdraw.Code
                );

                matchInBank.Quantity -= itemToWithdraw.Quantity;
            });
        }

        return new None();
    }

    public async Task<OneOf<AppError, List<CharacterJob>>> GetPreparationJob(
        PlayerCharacter character
    )
    {
        var itemsToObtain = LastFightSimResult
            .First(simResult => simResult.Schema.Name == character.Name)
            .ItemsToEquip;

        var bankItems = await GameState.Services.BankItemCache.GetBankItems(character);

        var itemsToWithdraw = FightMonster.GetItemsToWithdrawFromItemsToEquip(
            character,
            GameState,
            bankItems,
            itemsToObtain,
            false
        );

        var jobsNeededForNavigationResult =
            await character.PlayerActionService.NavigationService.GetJobsNeededForNavigation(
                Monster.Code
            );

        if (jobsNeededForNavigationResult.Value is AppError)
        {
            return jobsNeededForNavigationResult.AsT0;
        }

        List<CharacterJob> jobsNeededForNavigation = jobsNeededForNavigationResult.AsT1 ?? [];

        /**
        ** TODO: A bit of a hack for now - we know that the jobsNeededForNavigation are ObtainOrFindItem jobs, but those are
        ** "higher level jobs", i.e. they create other jobs, including withdraw jobs. In the current iteration, child jobs
        ** of jobs spawned by "collab jobs" will get the "collab id" on it.
        */
        //
        // jobsNeededForNavigation =
        // [
        //     .. jobsNeededForNavigation.Select(job =>
        //         (CharacterJob)new WithdrawItem(character, GameState, job.Code, job.Amount)
        //     ),
        // ];

        List<CharacterJob> jobs = BatchWithdrawJobsForEquipping(
                character,
                GameState,
                itemsToWithdraw
            )
            .ConvertAll(job => (CharacterJob)job);

        List<CharacterJob> resultJobs = [.. jobsNeededForNavigation.Union(jobs)];

        // resultJobs =
        // [
        //     .. resultJobs.Where(job =>
        //     {
        //         if (!job.JobName.Contains("WithdrawItem"))
        //         {
        //             return true;
        //         }

        //         var matchingItem = GameState.ItemsDict[job.Code];

        //         var inventoryResult = character.GetEquippedItemOrInInventory(job.Code);

        //         var amountEquippedOrInInventory = inventoryResult.Sum(itemInInventory =>
        //             itemInInventory.equipmentSlot.Quantity
        //         );

        //         bool shouldKeepJob = amountEquippedOrInInventory < job.Amount;

        //         if (!shouldKeepJob)
        //         {
        //             Logger.LogInformation(
        //                 $"{JobName}: [{character.Schema.Name}]: GetPreparationJob: Skipping withdrawing {job.Amount} x {job.Code} for fighting {Monster.Code} - already have this item"
        //             );
        //         }
        //         return shouldKeepJob;
        //     }),
        // ];

        return resultJobs;
    }

    static OneOf<AppError, JobsAndRequirementsForBoss> GetRequirementsJobIfTheyCanBeWithdrawn(
        BankDetails bankDetails,
        List<DropSchema> bankItems,
        List<CharacterJob> requiredJobs
    )
    {
        List<CharacterJob> finalJobs = [];
        List<DropSchema> itemsToWithdraw = [];
        int goldToWithdraw = 0;

        // It's okay here, should improve
        int allowedBudget = bankDetails.Gold;

        foreach (var job in requiredJobs)
        {
            if (job.JobName.Contains("WithdrawGold"))
            {
                if (job.Amount <= allowedBudget)
                {
                    finalJobs.Add(job);
                    goldToWithdraw += job.Amount;
                }
                else
                {
                    return new AppError(
                        $"Cannot do fight boss - has to withdraw more gold than allowed (need {job.Amount}, can withdraw {allowedBudget})"
                    );
                }
            }
            else
            {
                // Assume it's an item job, check if we can get enough in bank
                var amountInBank = bankItems
                    .FirstOrDefault(item => item.Code == job.Code)
                    ?.Quantity;

                if (amountInBank >= job.Amount)
                {
                    finalJobs.Add(job);
                    itemsToWithdraw.Add(new DropSchema { Code = job.Code, Quantity = job.Amount });
                }
                else
                {
                    return new AppError(
                        $"Cannot do any other jobs than withdrawing gold or items here, as it will take too long. Job was {job.Amount} x {job.Code} (name: {job.JobName})"
                    );
                }
            }
        }

        return new JobsAndRequirementsForBoss
        {
            Jobs = finalJobs,
            ItemsToWithdraw = itemsToWithdraw,
            GoldToWithdraw = goldToWithdraw,
        };
    }

    public static List<WithdrawItem> BatchWithdrawJobsForEquipping(
        PlayerCharacter character,
        GameState gameState,
        List<EquipmentSlot> itemsToEquip
    )
    {
        List<WithdrawItem> withdrawItems = [];

        // Just add a buffer, in case they pick up something else
        int availableInventorySpace = character.GetAvailableInventorySpace() - 5;

        if (availableInventorySpace <= 1)
        {
            throw new AppError("Out of inventory space in BatchWithdrawJobsForEqupping");
        }

        List<WithdrawItem> allJobs = [];

        foreach (var item in itemsToEquip)
        {
            List<WithdrawItem> jobsFromItem = [];

            /**
            ** Since we are likely equipping potions here - we can just split it in two,
            ** since we will equip them right after withdrawing them
            */
            if (item.Quantity > availableInventorySpace)
            {
                jobsFromItem.Add(
                    new WithdrawItem(
                        character,
                        gameState,
                        item.Code,
                        availableInventorySpace,
                        false
                    )
                );

                int amountLeft = item.Quantity - availableInventorySpace;

                jobsFromItem.Add(
                    new WithdrawItem(character, gameState, item.Code, amountLeft, false)
                );
            }
            else
            {
                jobsFromItem.Add(
                    new WithdrawItem(character, gameState, item.Code, item.Quantity, false)
                );
            }

            foreach (var job in jobsFromItem)
            {
                job.onAfterSuccessEndHook = async () =>
                {
                    await character.EquipItem(
                        new EquipRequest
                        {
                            Code = item.Code,
                            Quantity = job.Amount,
                            Slot = item.Slot,
                        }
                    );
                };

                allJobs.Add(job);
            }
        }

        return allJobs;
    }

    public bool ShouldStop()
    {
        // Stop the job after a timeout
        return false;
    }
}

public enum FightBossStatus
{
    New,
    Preparing,
    Fighting,

    Failed,
    Completed,
}

public enum CharacterFightBossStatus
{
    New,
    Preparing,
    ReadyToFight,
}

public record BossFightCharacterStatus
{
    public required PlayerCharacter Character { get; init; }
    public required CharacterFightBossStatus Status { get; set; }
}

public record JobsAndRequirementsForBoss
{
    public required List<CharacterJob> Jobs { get; set; }
    public required List<DropSchema> ItemsToWithdraw { get; set; }
    public required int GoldToWithdraw { get; set; }
}
