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

    private void SetupMakeCharacterCraftEvents(PlayerCharacter crafter, CraftItem lastJob)
    {
        logger.LogInformation(
            $"{JobName}: [{Character.Schema.Name}] setting up events to have {crafter.Schema.Name} craft {lastJob.Amount} x {lastJob.Code}"
        );

        var depositItems = SetupDepositAllMaterialsToBank(lastJob, crafter);

        foreach (var job in depositItems)
        {
            Character.QueueJob(job);
        }

        depositItems.Last().onSuccessEndHook += () =>
        {
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

            crafter.QueueJob(craftJob);

            return Task.Run(() => { });
        };
    }

    public void ForBank()
    {
        IsForBank = true;
    }

    private void SetupForBankEvents(CraftItem lastJob)
    {
        var jobs = SetupDepositAllMaterialsToBank(lastJob, null);

        foreach (var job in jobs)
        {
            Character.QueueJob(job);
        }
    }

    private List<DepositItems> SetupDepositAllMaterialsToBank(
        CraftItem lastJob,
        PlayerCharacter? crafter
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

            if (crafter is not null)
            {
                job.onSuccessEndHook = () =>
                {
                    logger.LogInformation(
                        $"{JobName}: [{Character.Schema.Name}] onSuccessEndHook: queueing job for {crafter.Schema.Name} to withdraw {material.Quantity} x {material.Code}"
                    );

                    crafter.QueueJob(
                        new WithdrawItem(crafter, gameState, material.Code, material.Quantity)
                    );

                    return Task.Run(() => { });
                };
            }
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

        if (AllowFindingItemInBank)
        {
            var accountRequester = GameServiceProvider
                .GetInstance()
                .GetService<AccountRequester>()!;

            var bankResult = await accountRequester.GetBankItems();

            if (bankResult is not BankItemsResponse bankItemsResponse)
            {
                return new AppError("Failed to get bank items");
            }

            itemsInBank = bankItemsResponse.Data;
        }
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
            SetupMakeCharacterCraftEvents(Crafter, craftJob);
        }

        return new None();
    }
}
