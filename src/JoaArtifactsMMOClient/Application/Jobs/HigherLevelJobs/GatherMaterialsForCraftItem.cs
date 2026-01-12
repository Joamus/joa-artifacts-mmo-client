using Application.ArtifactsApi.Schemas;
using Application.ArtifactsApi.Schemas.Responses;
using Application.Character;
using Application.Errors;
using Application.Services;
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

    private async Task SetupMakeCharacterCraftEvents(
        PlayerCharacter crafter,
        CharacterJob? jobBeforeCraft,
        CraftItem lastJob
    )
    {
        logger.LogInformation(
            $"{JobName}: [{Character.Schema.Name}] setting up events to have {crafter.Schema.Name} craft {lastJob.Amount} x {lastJob.Code}"
        );
        // CONSIDER ADDING A DEPOSIT UNNEEDED ITEMS HERE
        // This should probably be made a high prio job. I think it's gotten apparent that the crafter will spend too much time doing other stuff first.
        // We could even consider interrupting the job and resuming it, but it probably won't be bug-free :D

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
                await Character.QueueJob(job);
            }
        }

        logger.LogInformation(
            $"{JobName}: [{Character.Schema.Name}] queued {depositItems.Count} x deposit item jobs - last job before crafting ID: {(jobBeforeCraft is null ? "n/a" : jobBeforeCraft.Id)}"
        );

        // JobHook? currentOnSuccessEndHook = onSuccessEndHook;
        JobHook? currentAfterSuccessEndHook = onAfterSuccessEndHook;

        // onSuccessEndHook = null;
        onAfterSuccessEndHook = null;

        depositItems.Last().onSuccessEndHook = async () =>
        {
            logger.LogInformation(
                $"{JobName}: [{Character.Schema.Name}] onSuccessEndHook: last deposit job ran - queueing ObtainItem for crafter {crafter.Schema.Name} with ForCharacter({Character}) - {depositItems.Count} jobs, so they can craft {lastJob.Amount} x {lastJob.Code}"
            );
            var job = new ObtainItem(crafter, gameState, lastJob.Code, lastJob.Amount);
            job.AllowUsingMaterialsFromBank = true;

            job.ForCharacter(Character);

            job.onAfterSuccessEndHook = currentAfterSuccessEndHook;

            await crafter.QueueJob(job, true);

            if (DepositUnneededItems.ShouldInitDepositItems(crafter, true))
            {
                await crafter.QueueJob(new DepositUnneededItems(crafter, gameState), true);
            }
        };
    }

    public void ForBank()
    {
        IsForBank = true;
    }

    private async Task SetupForBankEvents(CraftItem lastJob)
    {
        var jobs = GetDepositAllMaterialsToBankJobs(lastJob);

        foreach (var job in jobs)
        {
            await Character.QueueJob(job);
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

        var bankResult = await gameState.BankItemCache.GetBankItems(Character);

        if (bankResult is not BankItemsResponse bankItemsResponse)
        {
            return new AppError("Failed to get bank items");
        }

        itemsInBank = bankItemsResponse.Data;

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

        // If we
        var preReqJob = await GetPreReqCraftedItemIfNeeded(
            Character,
            gameState,
            gameState.ItemsDict[Code],
            Amount
        );

        if (preReqJob is not null)
        {
            logger.LogInformation(
                $"{JobName}: [{Character.Schema.Name}] We need to obtain {preReqJob.Amount} x {preReqJob.Code} to get {Amount} x {Code}, but we shouldn't craft that ourselves - queueing this job first"
            );

            Character.QueueJobsBefore(Id, [preReqJob]);
            Status = JobStatus.Suspend;
            return new None();
        }

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
            await SetupForBankEvents(craftJob);
        }
        else if (Crafter is not null)
        {
            var jobBeforeCraft = jobs.Count > 0 ? jobs.Last() : null;
            await SetupMakeCharacterCraftEvents(Crafter, jobBeforeCraft, craftJob);
        }

        return new None();
    }

    /**
    ** We want to be able to handle if an item (like greater_dreadful_staff) requires having another item that requires a "crafting skill",
    ** because we want our crafter to craft both of those items, and not only the greater version. If we don't handle this, then the character
    ** that starts the job will craft the dreadful_staff, which we don't want them to.
    */
    async static Task<CharacterJob?> GetPreReqCraftedItemIfNeeded(
        PlayerCharacter character,
        GameState gameState,
        ItemSchema item,
        int itemQuantity
    )
    {
        if (item.Craft is null)
        {
            return null;
        }

        foreach (var craftIngredient in item.Craft.Items)
        {
            var matchingItem = gameState.ItemsDict[craftIngredient.Code];

            if (
                matchingItem.Craft is not null
                && SkillService.CraftingSkills.Contains(matchingItem.Craft.Skill)
                && !character.Roles.Contains(matchingItem.Craft.Skill)
            )
            {
                var result = character.GetEquippedItemOrInInventory(matchingItem.Code);

                (InventorySlot inventorySlot, bool isEquipped)? itemInInventory =
                    result.Count > 0 ? result.ElementAt(0)! : null;

                int amountToObtain = craftIngredient.Quantity * itemQuantity;

                if (itemInInventory is not null)
                {
                    // We can unequip the item here - we assume that we are getting the better version crafted to use ourselves.
                    if (itemInInventory.Value.isEquipped)
                    {
                        await character.UnequipItem(
                            itemInInventory.Value.inventorySlot.Code,
                            itemInInventory.Value.inventorySlot.Quantity
                        );
                    }
                    if (itemInInventory.Value.inventorySlot.Quantity >= amountToObtain)
                    {
                        continue;
                    }

                    amountToObtain -= itemInInventory.Value.inventorySlot.Quantity;
                }

                if (amountToObtain > 0)
                {
                    return new ObtainOrFindItem(
                        character,
                        gameState,
                        matchingItem.Code,
                        amountToObtain
                    );
                }
            }
        }

        return null;
    }
}
