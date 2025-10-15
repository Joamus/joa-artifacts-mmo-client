using Application;
using Application.ArtifactsApi.Schemas;
using Application.ArtifactsApi.Schemas.Responses;
using Application.Character;
using Application.Dtos;
using Application.Errors;
using Application.Jobs;
using Application.Services;
using Application.Services.ApiServices;
using Applicaton.Jobs;
using Applicaton.Services.FightSimulator;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public class GatherMaterialsForItem : CharacterJob
{
    public bool AllowUsingMaterialsFromBank { get; set; } = false;

    public bool AllowFindingItemInBank { get; set; } = true;
    public bool AllowUsingMaterialsFromInventory { get; set; } = true;

    public bool CanTriggerTraining { get; set; } = true;

    private List<DropSchema> itemsInBank { get; set; } = [];
    protected int Amount { get; init; }

    protected int _progressAmount { get; set; } = 0;

    public bool IsForBank { get; set; }

    public PlayerCharacter? Crafter { get; set; }

    public GatherMaterialsForItem(
        PlayerCharacter playerCharacter,
        GameState gameState,
        string code,
        int amount
    )
        : base(playerCharacter, gameState)
    {
        Code = code;
        Amount = amount;
    }

    private void SetupMakeCharacterCraftEvents(
        PlayerCharacter crafter,
        List<CharacterJob> allJobs,
        CraftItem lastJob
    )
    {
        logger.LogInformation(
            $"{JobName}: [{Character.Schema.Name}] setting up events to have {crafter.Schema.Name} craft {lastJob.Amount} x {lastJob.Code}"
        );

        var depositItems = GetDepositAllMaterialsToBankJobs(lastJob);

        // Should not crash
        var jobBeforeCraft = allJobs.Last();

        Character.QueueJobsAfter(jobBeforeCraft.Id, depositItems.Cast<CharacterJob>().ToList());

        logger.LogInformation(
            $"{JobName}: [{Character.Schema.Name}] queued {depositItems.Count} x deposit item jobs after job {jobBeforeCraft.Id} (last job before crafting)"
        );

        depositItems.Last().onSuccessEndHook = () =>
        {
            List<CharacterJob> jobsForCrafter = [];

            jobsForCrafter.Add(new DepositUnneededItems(crafter, gameState));

            logger.LogInformation(
                $"{JobName}: [{Character.Schema.Name}] onSuccessEndHook: last deposit job ran - queueing {depositItems.Count} x withdraw item jobs for the crafter {crafter.Schema.Name}, so they can craft {lastJob.Amount} x {lastJob.Code}"
            );

            foreach (var job in depositItems)
            {
                var withdrawItemJob = new WithdrawItem(crafter, gameState, job.Code, job._amount);
                withdrawItemJob.CanTriggerObtain = true;
                jobsForCrafter.Add(withdrawItemJob);
            }

            var craftJob = lastJob;
            craftJob.Character = crafter;

            craftJob.onSuccessEndHook = () =>
            {
                logger.LogInformation(
                    $"{JobName}: [{Character.Schema.Name}] onSuccessEndHook: queuing crafter {crafter.Schema.Name} depositting {lastJob.Amount} x {lastJob.Code} items, before {Character.Schema.Name} withdraws it"
                );
                var depositCraftItem = new DepositItems(
                    crafter,
                    gameState,
                    craftJob.Code,
                    craftJob.Amount
                );

                depositCraftItem.DontFailIfItemNotThere = true;

                depositCraftItem.onSuccessEndHook = () =>
                {
                    logger.LogInformation(
                        $"{JobName}: [{Character.Schema.Name}] onSuccessEndHook: queuing withdraw for {lastJob.Amount} x {lastJob.Code} items, that {crafter.Schema.Name} should have crafted"
                    );

                    Character.QueueJob(
                        new WithdrawItem(Character, gameState, lastJob.Code, lastJob.Amount, false),
                        true
                    );

                    return Task.Run(() => { });
                };

                crafter.QueueJob(depositCraftItem, true);
                return Task.Run(() => { });
            };

            jobsForCrafter.Add(craftJob);

            foreach (var job in jobsForCrafter)
            {
                crafter.QueueJob(job);
            }

            return Task.Run(() => { });
        };
    }

    public void ForBank()
    {
        IsForBank = true;
    }

    private void SetupForBankEvents(CraftItem lastJob)
    {
        var jobs = GetDepositAllMaterialsToBankJobs(lastJob);

        foreach (var job in jobs)
        {
            Character.QueueJob(job);
        }
    }

    private List<DepositItems> GetDepositAllMaterialsToBankJobs(
        CraftItem lastJob
    // PlayerCharacter? crafter
    )
    {
        var materials = gameState.ItemsDict.GetValueOrNull(lastJob.Code)?.Craft?.Items;

        if (materials is null)
        {
            throw new Exception($"Should never happen - the item isn't craftable");
        }

        List<DepositItems> depositItems = [];

        foreach (var material in materials)
        {
            // This could be a bit more efficient, if all of the queueing would only happen at once, the deposits should happen sequentially anyway
            var job = new DepositItems(
                Character,
                gameState,
                material.Code,
                material.Quantity
            ).SetParent<DepositItems>(this);

            job.DontFailIfItemNotThere = true;

            depositItems.Add(job);
        }

        return depositItems;
    }

    protected override async Task<OneOf<AppError, None>> ExecuteAsync()
    {
        logger.LogInformation($"{JobName}: [{Character.Schema.Name}] run started");

        if (DepositUnneededItems.ShouldInitDepositItems(Character))
        {
            Character.QueueJobsBefore(Id, [new DepositUnneededItems(Character, gameState)]);
            Status = JobStatus.Suspend;
            return new None();
        }

        if (Character.Schema.Name == Crafter?.Schema.Name)
        {
            return new AppError(
                $"{JobName}: [{Character.Schema.Name}] the crafter is the same as the creator - skipping"
            );
        }

        List<CharacterJob> jobs = [];
        logger.LogInformation(
            $"{JobName}: [{Character.Schema.Name}] run started - progress {Code} ({_progressAmount}/{Amount})"
        );

        // if (AllowFindingItemInBank)
        // {
        var accountRequester = GameServiceProvider.GetInstance().GetService<AccountRequester>()!;

        var bankResult = await accountRequester.GetBankItems();

        if (bankResult is not BankItemsResponse bankItemsResponse)
        {
            return new AppError("Failed to get bank items");
        }

        itemsInBank = bankItemsResponse.Data;
        // }
        // useItemIfInInventory is set to the job's value at first, so we can allow obtaining an item we already have.
        // But if we have the ingredients in our inventory, then we should always use them (for now).
        // Having this variable will allow us to e.g craft multiple copper daggers, else we could only have 1 in our inventory

        var result = await ObtainItem.GetJobsRequired(
            Character,
            gameState,
            AllowUsingMaterialsFromBank,
            itemsInBank,
            jobs,
            Code,
            Amount,
            AllowUsingMaterialsFromInventory,
            CanTriggerTraining
        );

        logger.LogInformation(
            $"{JobName}: [{Character.Schema.Name}] found {jobs.Count} jobs to run, to gather materials for item {Code}"
        );

        switch (result.Value)
        {
            case AppError jobError:
                return jobError;
        }

        var lastJob = jobs.Last();

        if (lastJob is null || lastJob is not CraftItem)
        {
            return new AppError(
                $"{JobName}: [{Character.Schema.Name}] error - last job is null or not a CraftItem job"
            );
        }

        var craftJob = (CraftItem)lastJob;

        // Remove the last job, we don't want to craft it
        jobs.RemoveAt(jobs.Count() - 1);

        Character.QueueJobsAfter(Id, jobs);

        if (IsForBank)
        {
            if (lastJob is null || lastJob is not CraftItem)
            {
                return new AppError(
                    $"{JobName}: [{Character.Schema.Name}] error - last job is null or not a CraftItem job"
                );
            }
            SetupForBankEvents(craftJob);
        }
        else if (Crafter is not null)
        {
            SetupMakeCharacterCraftEvents(Crafter, jobs, craftJob);
        }

        return new None();
    }
}
