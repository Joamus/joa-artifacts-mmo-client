using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Application;
using Application.ArtifactsApi.Schemas;
using Application.ArtifactsApi.Schemas.Requests;
using Application.ArtifactsApi.Schemas.Responses;
using Application.Character;
using Application.Errors;
using Application.Services;
using Applicaton.Jobs;
using Applicaton.Services.FightSimulator;
using Microsoft.OpenApi.Extensions;
using OneOf;
using OneOf.Types;

namespace Application.Jobs.Orchestrators;

public class FightBossOrchestrator
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string JobName { get; private set; } = "FightBoss";

    [JsonIgnore]
    private static ILogger Logger { get; set; } =
        AppLogger.loggerFactory.CreateLogger<FightBossOrchestrator>();

    [JsonIgnore]
    private readonly SemaphoreSlim GetNextJobLock = new(1, 1);

    [JsonIgnore]
    public const int CHARACTERS_IN_BOSS_FIGHT = 3;

    [JsonIgnore]
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

    // Cancellation token source is for cancelling promises when the job completes
    [JsonIgnore]
    readonly CancellationTokenSource CancellationTokenSource = new();

    public bool AllowUsingMaterialsFromInventory = false;

    [JsonIgnore]
    public required GameState GameState { get; set; }
    public required List<PlayerCharacter> OtherCharacters { get; set; }
    public required List<PlayerCharacter> AllCharacters { get; set; }
    public required List<BossFightCharacterStatus> AllCharactersStatuses { get; set; }

    [JsonIgnore]
    public required List<FightSimResult> LastFightSimResult { get; set; }

    public static async Task<OneOf<AppError, FightBossOrchestrator>> InitializeFightBossJob(
        InitializeFightBossJobParams jobParams
    )
    {
        var character = jobParams.Character;
        var gameState = jobParams.GameState;
        var otherCharacters = jobParams.OtherCharacters;
        var monster = jobParams.Monster;
        var itemCode = jobParams.ItemCode;
        var amount = jobParams.Amount;
        var allowUsingMaterialsFromInventory = jobParams.AllowUsingMaterialsFromInventory;

        Logger.LogInformation(
            $"FightBoss: [{character.Schema.Name}]: Initializing fight boss job to fight {monster.Code}"
        );

        var result = await GetLastFightSimAndRequirements(
            character,
            otherCharacters,
            gameState,
            monster,
            false
        );

        List<FightSimResult> fightSim = [];

        if (result.IsT0)
        {
            return result.AsT0;
        }
        else
        {
            fightSim = result.AsT1;
        }

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

        int initialAmont = 0;

        if (mode == JobMode.Gather && jobParams.AllowUsingMaterialsFromInventory)
        {
            initialAmont = character.GetItemFromInventory(itemCode!)?.Quantity ?? 0;
        }

        var job = new FightBossOrchestrator
        {
            MainCharacter = character,
            GameState = gameState,
            OtherCharacters = otherCharacters,

            AllCharacters = allCharacters,

            LastFightSimResult = fightSim,

            AllCharactersStatuses = allCharactersStatuses,
            AllowUsingMaterialsFromInventory = jobParams.AllowUsingMaterialsFromInventory,

            Monster = monster,
            Mode = mode,
            ItemCode = itemCode,
            Amount = amount,
            InitialAmount = initialAmont,
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

            return new AppError(
                $"FightBoss: [{character.Schema.Name}]: Failed to initialize fight boss job to fight {monster.Code} - stopping"
            );
        }

        Logger.LogInformation(
            $"FightBoss: [{character.Schema.Name}]: Initialized fight boss job to fight {monster.Code}"
        );
        return job;
    }

    void RemoveCharacterFromJob(PlayerCharacter character)
    {
        if (character.CurrentJobOrchestrator?.Id == Id)
        {
            character.CurrentJobOrchestrator = null;
        }
    }

    public void Disband(string reason, bool successful)
    {
        // Assume that a job is failed when we remove people
        Status = successful ? FightBossStatus.Completed : FightBossStatus.Failed;

        CancellationTokenSource.Cancel();

        Logger.LogInformation(
            $"{JobName}: Disbanding group - status: {Status.GetDisplayName()} - reason {reason}"
        );

        foreach (var character in AllCharacters)
        {
            RemoveCharacterFromJob(character);
        }

        // AllCharacters = [];
        // AllCharactersStatuses = [];
        // OtherCharacters = [];
    }

    void HandleError(string errorReason)
    {
        Disband(errorReason, false);
    }

    public async Task<OneOf<AppError, List<CharacterJob>>> GetNextJobs(PlayerCharacter character)
    {
        try
        {
            await GetNextJobLock.WaitAsync();
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
        finally
        {
            GetNextJobLock.Release();
        }
    }

    async Task<OneOf<AppError, List<CharacterJob>>> InnerGetNextJobs(PlayerCharacter character)
    {
        try
        {
            List<CharacterJob> nextPreparationJobs = [];

            // Dirty hack, but we cannot just return [] without casting
            List<CharacterJob> emptyJobs = [];

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

            // Only allow main character to start the fight
            if (AreAllReadyToFightBoss())
            {
                if (character.Name == MainCharacter.Name)
                {
                    Logger.LogInformation(
                        $"{JobName}: [{character.Schema.Name}]: Detected that the group is ready to fight {Monster.Code}.."
                    );
                    Status = FightBossStatus.Fighting;
                    await StartBossFight().WaitAsync(CancellationTokenSource.Token);

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

                    ResetCharacterStatuses();
                    Status = FightBossStatus.New;
                    /**
                    ** We don't neccessarily need the return here, we could just fall through and give the next job,
                    ** but it's just easier to understand that they are different flows
                    */
                }
                return emptyJobs;
            }

            switch (GetCharacterStatus(character))
            {
                case CharacterFightBossStatus.New:
                    var nextPreparationJobResult = await GetPreparationJobAndItemsToEquip(
                        character
                    );

                    if (nextPreparationJobResult.IsT0)
                    {
                        return nextPreparationJobResult.AsT0;
                    }

                    nextPreparationJobs = nextPreparationJobResult.AsT1.Jobs;

                    List<EquipRequest> equipRequests =
                    [
                        .. nextPreparationJobResult.AsT1.ItemsToEquip.Select(
                            item => new EquipRequest
                            {
                                Code = item.Code,
                                Quantity = item.Quantity,
                                Slot = item.Slot,
                            }
                        ),
                    ];

                    if (equipRequests.Count > 0)
                    {
                        await character.EquipItems(equipRequests);
                    }
                    // Set this fight boss job as the parent collab job of all jobs we return.
                    nextPreparationJobs.ForEach(job => job.ParentCollabJobId = Id);

                    /**
                    ** The character's status should be "New" here, and after they have done these jobs, they should be ready in preparing.
                    ** They reason that we have this Preparing state, is to prevent another character from just starting the fight, even though
                    ** they haven't done the jobs they need to do yet.
                    */
                    SetCharacterStatus(character, CharacterFightBossStatus.Preparing);

                    return nextPreparationJobs;
                case CharacterFightBossStatus.Preparing:
                    Logger.LogInformation(
                        $"{JobName}: [{character.Schema.Name}]: Status was \"preparing\" - changing status to \"ready to fight\" - fighting {Monster.Code}"
                    );
                    SetCharacterStatus(character, CharacterFightBossStatus.ReadyToFight);
                    break;
                case CharacterFightBossStatus.ReadyToFight:
                    Logger.LogInformation(
                        $"{JobName}: [{character.Schema.Name}]: Is ready to fight, but waiting for others - fighting {Monster.Code}"
                    );
                    break;
            }

            return emptyJobs;
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
    }

    bool AreAllReadyToFightBoss()
    {
        return AllCharactersStatuses.All(status =>
            status.Status == CharacterFightBossStatus.ReadyToFight
        );
    }

    void SetCharacterStatus(PlayerCharacter character, CharacterFightBossStatus status)
    {
        AllCharactersStatuses
            .FirstOrDefault(status => status.Character.Name == character.Name)
            ?.Status = status;
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

        if (IsJobDone())
        {
            return;
        }

        var preFightRoutinesForAllCharacters = AllCharacters
            .Select(PreFightRoutineForCharacter)
            .ToList();

        await Task.WhenAll(preFightRoutinesForAllCharacters);

        Logger.LogInformation(
            $"{JobName}: StartBossFight: Pre-fight routines are done - fighting {Monster.Code}"
        );

        if (IsJobDone())
        {
            return;
        }

        await MainCharacter.Fight(OtherCharacters);

        Logger.LogInformation($"{JobName}: StartBossFight: Round against {Monster.Code} done");

        // Try first without new bank items, to save time getting slight upgrades.

        bool skipItemsToEquip = true;

        var fightSimResult = await GetLastFightSimAndRequirements(
            MainCharacter,
            OtherCharacters,
            GameState,
            Monster,
            true
        );

        if (fightSimResult.IsT0)
        {
            var newFightSimResult = await GetLastFightSimAndRequirements(
                MainCharacter,
                OtherCharacters,
                GameState,
                Monster,
                false
            );

            skipItemsToEquip = false;

            fightSimResult = newFightSimResult;
        }

        // Ugly, but double guard so we can fail the job
        if (IsJobDone())
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

                // We only want them to go back if it's needed due to potions. So if they want to swap to other items,
                // it's OK, as long as they are also getting other potions. If they aren't getting potions, skip the entire thing,
                // since we might have to pay money/items to get back to the boss again, and it's probably won't make a big difference
                var simResultWithCorrectedItemsToEquip = simResult
                    .Select(result =>
                    {
                        if (skipItemsToEquip)
                        {
                            return result with { ItemsToEquip = [] };
                        }

                        bool itemsToEquipContainsPotions = result.ItemsToEquip.Exists(item =>
                        {
                            var matchingItem = GameState.ItemsDict[item.Code];

                            return matchingItem.Type == "utility";
                        });

                        var newItemsToEquip = itemsToEquipContainsPotions
                            ? result.ItemsToEquip
                            : [];

                        var newResult = result with { ItemsToEquip = newItemsToEquip };

                        return newResult;
                    })
                    .ToList();

                LastFightSimResult = simResultWithCorrectedItemsToEquip;
            }
        );
    }

    async Task PreFightRoutineForCharacter(PlayerCharacter character)
    {
        await character.WaitForCooldown();

        await FightMonster.HealIfNotAtFullHp(character, GameState, true);

        await character.NavigateTo(Monster.Code);

        await character.WaitForCooldown();
    }

    public static List<PlayerCharacter> GetBestCandidatesToFight(
        PlayerCharacter character,
        GameState gameState
    )
    {
        List<PlayerCharacter> otherAvailablePlayers =
        [
            .. gameState
                .Characters.Where(otherCharacter =>
                    otherCharacter.Schema.Name != character.Schema.Name
                    && (otherCharacter.CurrentJobOrchestrator is null)
                    // A bit wonky, but we should only take characters with us who have enough inventory space
                    && (!DepositUnneededItems.ShouldInitDepositItems(otherCharacter, false))
                )
                .OrderByDescending((b) => b.Schema.Level),
        ];

        // Allow partial groups, e.g. only 2, since there might be cases where they can win
        int amountToRecruit = Math.Min(otherAvailablePlayers.Count, CHARACTERS_IN_BOSS_FIGHT - 1);

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
            monster,
            false
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
        MonsterSchema monster,
        bool skipBankItems
    )
    {
        var bankItems = skipBankItems
            ? []
            : await gameState.Services.BankItemCache.GetBankItems(character);
        var bankDetails = await gameState.Services.BankItemCache.GetBankDetails();

        if (gameState.Services.EventService.IsEntityFromEventThatIsUnavailable(monster.Code))
        {
            return new AppError(
                $"Cannot not fight boss {monster.Code}, since it is an event boss that is not available"
            );
        }

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
                mutatingBankDetails,
                mutatingBankItems,
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

        if (mutatingBankDetails.Gold < 0 || mutatingBankItems.Exists(item => item.Quantity < 0))
        {
            return new AppError(
                $"Not all item/gold requirements can be fulfilled to go defeat {monster.Code}"
            );
        }

        return new None();
    }

    public async Task<
        OneOf<AppError, (List<CharacterJob> Jobs, List<EquipmentSlot> ItemsToEquip)>
    > GetPreparationJobAndItemsToEquip(PlayerCharacter character)
    {
        var itemsToEquipFromSim = LastFightSimResult
            .First(simResult => simResult.Schema.Name == character.Name)
            .ItemsToEquip;

        var bankItems = await GameState.Services.BankItemCache.GetBankItems(character);

        var itemsToWithdraw = FightMonster.GetItemsToWithdrawFromItemsToEquip(
            character,
            GameState,
            bankItems,
            itemsToEquipFromSim,
            false
        );

        // We need to get the required items from the bank, and not if where we currently might be,
        // which could be at the boss.
        bool goingBackToBank = itemsToWithdraw.Count > 0;

        MapSchema? closestBank = null;

        if (goingBackToBank)
        {
            var steps = character
                .PlayerActionService.NavigationService.GetAllStepsToDestination("bank")
                .AsT1.Steps;

            if (steps.Count > 0)
            {
                closestBank = steps.Last().NewMap;
            }
        }

        var jobsNeededForNavigationResult =
            await character.PlayerActionService.NavigationService.GetJobsNeededForNavigation(
                Monster.Code,
                closestBank
            );

        if (jobsNeededForNavigationResult.Value is AppError)
        {
            return jobsNeededForNavigationResult.AsT0;
        }

        List<CharacterJob> jobsNeededForNavigation = jobsNeededForNavigationResult.AsT1 ?? [];

        List<EquipmentSlot> itemsToEquipButNotWithdraw =
        [
            .. itemsToEquipFromSim.Where(itemToEquip =>
                !itemsToWithdraw.Exists(itemToWithdraw => itemToWithdraw.Code == itemToEquip.Code)
            ),
        ];

        List<CharacterJob> jobs = BatchWithdrawJobsForEquipping(
                character,
                GameState,
                itemsToWithdraw
            )
            .ConvertAll(job => (CharacterJob)job);

        // if (DepositUnneededItems.ShouldInitDepositItems(character, true))
        // {
        //     jobs.Add(new DepositUnneededItems(character, GameState, null, true));
        // }

        List<CharacterJob> resultJobs = [.. jobsNeededForNavigation.Union(jobs)];

        return (Jobs: resultJobs, ItemsToEquip: itemsToEquipButNotWithdraw);
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

    public static async Task<BossGrindDetails?> GetOtherCharactersToMaximizeXpAgainstBoss(
        PlayerCharacter character,
        List<PlayerCharacter> otherCharacters,
        GameState gameState,
        MonsterSchema monster,
        List<DropSchema> bankItems
    )
    {
        // int maxCharactersInBossFight = 3;

        // List<(List<PlayerCharacter> AllCharacters, int XpPerKill)> results = [];

        // Hack until we feel like writing a recursive function.
        // Note, this only works as long as boss fights is max 3 characters, and we have 5 characters in total
        List<List<PlayerCharacter>> combinations =
        [
            [character, otherCharacters[0], otherCharacters[1]],
            [character, otherCharacters[0], otherCharacters[2]],
            [character, otherCharacters[0], otherCharacters[3]],
            [character, otherCharacters[1], otherCharacters[2]],
            [character, otherCharacters[1], otherCharacters[3]],
            [character, otherCharacters[2], otherCharacters[3]],
        ];

        List<BossGrindDetails> results = [];

        foreach (var combination in combinations)
        {
            var otherChars = combination
                .Where(combinationChar => combinationChar.Name != character.Name)
                .ToList();

            int averageLevel = otherChars.Sum(player => player.Schema.Level) / otherChars.Count;

            int xpForKill = CalculationService.GetXpForFight(
                character.Schema,
                otherChars.Select(player => player.Schema.Level).ToList(),
                monster
            );

            if (xpForKill == 0)
            {
                continue;
            }

            var fightSimResults = FightSimulator.SimulateBossFightOutcome(
                character,
                otherChars,
                gameState,
                bankItems,
                monster
            );

            if (
                fightSimResults.All(simResult => simResult.Outcome.ShouldFight)
                && await CanFulfillRequirementsForFightingBoss(
                    character,
                    otherCharacters,
                    gameState,
                    monster
                )
            )
            {
                results.Add(
                    new BossGrindDetails
                    {
                        AllCharacters = combination,
                        MainCharacter = character,
                        OtherCharacters = otherChars,
                        Monster = monster,
                        FightSimResults = fightSimResults,
                        XpPerKill = xpForKill,
                    }
                );
            }
        }
        // List<List<PlayerCharacter>> knownCombinations = [];

        // bool IsCombinationKnown(List<PlayerCharacter> input)
        // {
        //     return knownCombinations.Exists(combination =>
        //         combination.All(charInCombination =>
        //             input.Exists(startListChar => startListChar.Name == charInCombination.Name)
        //         )
        //     );
        // }

        // foreach (var otherCharacter in otherCharacters)
        // {
        //     List<PlayerCharacter> startList = [character, otherCharacter];

        //     if (IsCombinationKnown(startList))
        //     {
        //         continue;
        //     }

        //     knownCombinations.Add(startList);

        //     int charactersNeeded = maxCharactersInBossFight - startList.Count;

        //     while (charactersNeeded > 0)
        //     {
        //         List<PlayerCharacter> proposedList = [.. startList];

        //         foreach (var otherCharacter2 in otherCharacters)
        //         {
        //             List<PlayerCharacter> proposedList = [.. startList, otherCharacter2];
        //         }
        //     }
        // }

        return results.OrderByDescending(element => element.XpPerKill).FirstOrDefault();
    }

    public static async Task<BossGrindDetails?> FindBossMonsterCandidateForXp(
        PlayerCharacter character,
        GameState gameState,
        List<DropSchema> bankItems
    )
    {
        List<MonsterSchema> bossCandidates =
        [
            .. gameState.Monsters.Where(monster =>
            {
                if (
                    monster.Type != MonsterType.Boss
                    || gameState.Services.EventService.IsEntityFromEventThatIsUnavailable(
                        monster.Code
                    )
                )
                {
                    return false;
                }

                int lowestLevelBound =
                    character.Schema.Level - PlayerActionService.LEVEL_DIFF_NO_XP;

                int highestLevelBound =
                    character.Schema.Level + PlayerActionService.LEVEL_DIFF_NO_XP;

                bool isInLevelRange =
                    monster.Level >= lowestLevelBound && monster.Level <= highestLevelBound;

                return isInLevelRange;
            }),
        ];

        List<PlayerCharacter> otherCharacters =
        [
            .. gameState.Characters.Where(gameStateChar => gameStateChar.Name != character.Name),
        ];

        List<BossGrindDetails> outcomes = [];

        foreach (var bossCandidate in bossCandidates)
        {
            var result = await GetOtherCharactersToMaximizeXpAgainstBoss(
                character,
                otherCharacters,
                gameState,
                bossCandidate,
                bankItems
            );

            if (result is not null)
            {
                outcomes.Add(result);
            }
        }

        var bestOutcome = outcomes
            .OrderByDescending(outcome => outcome.XpPerKill)
            .FirstOrDefault(outcome =>
                outcome.OtherCharacters.All(otherCharacter =>
                    otherCharacter.CurrentJobOrchestrator is null
                )
            );

        return bestOutcome;
    }

    bool IsJobDone()
    {
        return Status == FightBossStatus.Failed || Status == FightBossStatus.Completed;
    }
}

public enum FightBossStatus
{
    New,
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

public record BossGrindDetails
{
    public required PlayerCharacter MainCharacter { get; init; }
    public required List<PlayerCharacter> AllCharacters { get; init; }
    public required List<PlayerCharacter> OtherCharacters { get; init; }

    public required MonsterSchema Monster { get; init; }
    public required int XpPerKill { get; init; }
    public required List<FightSimResult> FightSimResults { get; init; }
}
