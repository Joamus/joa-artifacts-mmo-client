using Application.Character;
using Application.Dtos;
using Application.Errors;
using Application.Services;
using Applicaton.Jobs;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public class ItemTask : CharacterJob
{
    public string? ItemCode { get; set; }
    public int? ItemAmount { get; set; }

    public bool CanTriggerTraining { get; set; } = true;

    public ItemTask(
        PlayerCharacter playerCharacter,
        GameState gameState,
        string? itemCode = null,
        int? itemAmount = null
    )
        : base(playerCharacter, gameState)
    {
        ItemCode = itemCode;
        ItemAmount = itemAmount;
    }

    public void ForBank()
    {
        onSuccessEndHook = async () =>
        {
            logger.LogInformation($"{JobName}: [{Character.Schema.Name}] onSuccessHook: running");

            var taskCoinsAmount =
                Character.GetItemFromInventory(ItemService.TasksCoin)?.Quantity ?? 0;

            if (taskCoinsAmount > 0)
            {
                logger.LogInformation(
                    $"{JobName}: [{Character.Schema.Name}] onSuccessHook: found {taskCoinsAmount} task coins - queue depositing them"
                );
                await Character.QueueJob(
                    new DepositItems(Character, gameState, ItemService.TasksCoin, taskCoinsAmount),
                    true
                );
            }

            if (ItemCode is not null && ItemAmount is not null)
            {
                logger.LogInformation(
                    $"{JobName}: [{Character.Schema.Name}] onSuccessHook: found {ItemAmount} x {ItemCode} - queue depositing them"
                );
                await Character.QueueJob(
                    new DepositItems(Character, gameState, ItemCode, (int)ItemAmount),
                    true
                );
            }
        };
    }

    protected override async Task<OneOf<AppError, None>> ExecuteAsync()
    {
        logger.LogInformation($"{JobName}: [{Character.Schema.Name}] run started");

        if (DepositUnneededItems.ShouldInitDepositItems(Character))
        {
            await Character.QueueJobsBefore(Id, [new DepositUnneededItems(Character, gameState)]);
            Status = JobStatus.Suspend;
            return new None();
        }

        List<CharacterJob> jobs = [];

        if (Character.Schema.TaskType == "")
        {
            // Go pick up task - then we should continue
            await Character.QueueJobsBefore(
                Id,
                [
                    new AcceptNewTask(
                        Character,
                        gameState,
                        TaskType.items
                    ).SetParent<AcceptNewTask>(this),
                ]
            );
            Status = JobStatus.Suspend;
            return new None();
        }

        // For gather tasks, we are only going to get tasks that we have high enough skill to do, which means that it shouldn't be needed
        // to check if we have enough skill etc. to complete the task
        if (Character.Schema.TaskType != TaskType.items.ToString())
        {
            return new AppError(
                $"Cannot do a {JobName}, because the current task is {Character.Schema.TaskType}"
            );
        }

        int progressAmount = Character.Schema.TaskProgress;
        int amount = Character.Schema.TaskTotal;

        int remainingToGather = amount - progressAmount;

        if (remainingToGather > 0)
        {
            string itemCode = Character.Schema.Task;

            // We can be told to do a job that requires us to gather more items than we can carry.
            // We queue the job to gather and deposit it before the current job, so we essentially loop this until we are done
            // For now, DepositUnneededItems should also take into account to give things to the tasks master, as a fallback.

            int amountInInventory = Character.GetItemFromInventory(itemCode)?.Quantity ?? 0;

            int amountToObtain = Math.Min(Character.GetInventorySpaceLeft() - 1, remainingToGather);

            if (amountInInventory >= amountToObtain)
            {
                logger.LogInformation(
                    $"{JobName}: [{Character.Schema.Name}]: Found {amountInInventory} x {Code} in inventory - trading in those"
                );
                await Character.NavigateTo("items");
                await Character.TaskTrade(itemCode, Math.Min(amountInInventory, amountToObtain));

                amountToObtain = 0;
            }
            else
            {
                amountToObtain -= amountInInventory;

                if (amountInInventory > 0)
                {
                    logger.LogInformation(
                        $"{JobName}: [{Character.Schema.Name}]: Found {amountInInventory} x {Code} in inventory - subtracting those from amount to obtain"
                    );
                }
            }

            if (amountToObtain > 0)
            {
                var matchingItem = gameState.ItemsDict.GetValueOrNull(Character.Schema.Task)!;

                if (matchingItem is null)
                {
                    return new AppError(
                        $"{JobName}: [{Character.Schema.Name}] app error: Could not find {Character.Schema.Task} item in items dict"
                    );
                }

                List<int> iterations = ObtainItem.CalculateObtainItemIterations(
                    matchingItem,
                    Character,
                    amountToObtain
                );

                logger.LogInformation(
                    $"{JobName}: [{Character.Schema.Name}]: Need to gather {progressAmount} of {amount} x {Code} - gathering and trading {amountToObtain}, then retriggering this job"
                );

                foreach (var iterationAmount in iterations)
                {
                    var job = new ObtainOrFindItem(
                        Character,
                        gameState,
                        Character.Schema.Task,
                        iterationAmount
                    );

                    logger.LogInformation(
                        $"{JobName}: [{Character.Schema.Name}]: Queueing obtaining {iterationAmount} of {amountToObtain} - total iterations will be {iterations.Count}"
                    );

                    job.CanTriggerTraining = CanTriggerTraining;
                    job.AllowUsingMaterialsFromBank = true;

                    job.onSuccessEndHook = async () =>
                    {
                        int amountInInventory =
                            Character.GetItemFromInventory(itemCode)?.Quantity ?? 0;

                        logger.LogInformation(
                            $"{JobName}: [{Character.Schema.Name}]: onSuccessEndHook for obtain item - trading {amountInInventory} (skip if 0)"
                        );

                        int progressAmount = Character.Schema.TaskProgress;
                        int amount = Character.Schema.TaskTotal;

                        // Being absolutely sure that we are still working on this task.
                        int remainingToGather =
                            Character.Schema.Task == itemCode
                                ? Character.Schema.TaskTotal - Character.Schema.TaskProgress
                                : 0;

                        if (amountInInventory > 0 && remainingToGather > 0)
                        {
                            await Character.NavigateTo("items");
                            await Character.TaskTrade(
                                itemCode,
                                Math.Min(amountInInventory, remainingToGather)
                            );
                        }
                    };

                    jobs.Add(job);
                }
                await Character.QueueJobsBefore(Id, jobs);
                Status = JobStatus.Suspend;
                return new None();
            }
        }

        var completeTask = new CompleteTask(
            Character,
            gameState,
            ItemCode,
            ItemAmount
        ).SetParent<CompleteTask>(this);

        jobs.Add(completeTask);

        completeTask.onSuccessEndHook = onSuccessEndHook;

        await Character.QueueJobsAfter(Id, jobs);

        // Reset it
        onSuccessEndHook = null;

        logger.LogInformation(
            $"{JobName}: [{Character.Schema.Name}] - found {jobs.Count} jobs to run, to complete task {Code}"
        );

        return new None();
    }
}
