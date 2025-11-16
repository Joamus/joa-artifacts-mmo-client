using Application.ArtifactsApi.Schemas;
using Application.ArtifactsApi.Schemas.Responses;
using Application.Character;
using Application.Errors;
using Applicaton.Jobs;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public class GatherMaterialsForItem : CharacterJob
{
    public bool AllowUsingMaterialsFromBank { get; set; } = true;

    public bool AllowFindingItemInBank { get; set; } = true;
    public bool AllowUsingMaterialsFromInventory { get; set; } = true;

    public bool CanTriggerTraining { get; set; } = true;

    private List<DropSchema> itemsInBank { get; set; } = [];

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
        CharacterJob? jobBeforeCraft,
        CraftItem lastJob
    )
    {
        logger.LogInformation(
            $"{JobName}: [{Character.Schema.Name}] setting up events to have {crafter.Schema.Name} craft {lastJob.Amount} x {lastJob.Code}"
        );

        var depositItems = GetDepositAllMaterialsToBankJobs(lastJob);

        if (jobBeforeCraft is not null)
        {
            Character.QueueJobsAfter(jobBeforeCraft.Id, depositItems.Cast<CharacterJob>().ToList());
        }
        else
        {
            // This scenario can happen if the character has all of the items in their inventory, at the time of writing this,
            // I think this scenario is caused by a bug
            foreach (var job in depositItems)
            {
                Character.QueueJob(job);
            }
        }

        logger.LogInformation(
            $"{JobName}: [{Character.Schema.Name}] queued {depositItems.Count} x deposit item jobs - last job before crafting ID: {(jobBeforeCraft is null ? "n/a" : jobBeforeCraft.Id)}"
        );

        depositItems.Last().onSuccessEndHook = () =>
        {
            logger.LogInformation(
                $"{JobName}: [{Character.Schema.Name}] onSuccessEndHook: last deposit job ran - queueing ObtainItem for crafter {crafter.Schema.Name} with ForCharacter({Character}) - {depositItems.Count} jobs, so they can craft {lastJob.Amount} x {lastJob.Code}"
            );
            var job = new ObtainItem(crafter, gameState, lastJob.Code, lastJob.Amount);
            // job.AllowFindingItemInBank = true;
            job.AllowUsingMaterialsFromBank = true;

            job.ForCharacter(Character);

            crafter.QueueJob(job);
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
                material.Quantity * lastJob.Amount
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

        var bankResult = await gameState.BankItemCache.GetBankItems(Character);

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

        // Adding to the wish list - basically until the crafter creates the item, so we know it's in progress.
        Character.AddToWishlist(Code, Amount);

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
            var jobBeforeCraft = jobs.Count > 0 ? jobs.Last() : null;
            SetupMakeCharacterCraftEvents(Crafter, jobBeforeCraft, craftJob);
        }

        return new None();
    }
}
