using Application.ArtifactsApi.Schemas;
using Application.Character;
using Application.Dtos;
using Application.Errors;
using Applicaton.Services.FightSimulator;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public class ItemTask : CharacterJob
{
    public string? ItemCode { get; set; }
    public int? ItemAmount { get; set; }

    public bool CanTriggerTraining { get; set; }

    public ItemTask(
        PlayerCharacter playerCharacter,
        GameState gameState,
        string? itemCode,
        int? itemAmount
    )
        : base(playerCharacter, gameState)
    {
        ItemCode = itemCode;
        ItemAmount = itemAmount;
    }

    public void ForBank()
    {
        onSuccessEndHook = () =>
        {
            logger.LogInformation($"{JobName}: [{Character.Schema.Name}] onSuccessHook: running");

            var taskCoinsAmount = Character.GetItemFromInventory("tasks_coins")?.Quantity ?? 0;

            if (taskCoinsAmount > 0)
            {
                logger.LogInformation(
                    $"{JobName}: [{Character.Schema.Name}] onSuccessHook: found {taskCoinsAmount} task coins - queue depositing them"
                );
                Character.QueueJob(
                    new DepositItems(Character, gameState, "tasks_coins", taskCoinsAmount),
                    true
                );
            }

            if (ItemCode is not null && ItemAmount is not null)
            {
                logger.LogInformation(
                    $"{JobName}: [{Character.Schema.Name}] onSuccessHook: found {ItemAmount} x {ItemCode} - queue depositing them"
                );
                Character.QueueJob(
                    new DepositItems(Character, gameState, ItemCode, (int)ItemAmount),
                    true
                );
            }

            return Task.Run(() => { });
        };
    }

    protected override Task<OneOf<AppError, None>> ExecuteAsync()
    {
        logger.LogInformation($"{JobName}: [{Character.Schema.Name}] run started");

        List<CharacterJob> jobs = [];

        if (Character.Schema.TaskType == "")
        {
            // Go pick up task - then we should continue
            Character.QueueJobsBefore(
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
            return Task.FromResult<OneOf<AppError, None>>(new None());
        }

        // For gather tasks, we are only going to get tasks that we have high enough skill to do, which means that it shouldn't be needed
        // to check if we have enough skill etc. to complete the task
        if (Character.Schema.TaskType != TaskType.items.ToString())
        {
            return Task.FromResult<OneOf<AppError, None>>(
                new AppError(
                    $"Cannot do a {JobName}, because the current task is {Character.Schema.TaskType}"
                )
            );
        }

        int progressAmount = Character.Schema.TaskProgress;
        int amount = Character.Schema.TaskTotal;

        int remainingToGather = amount - progressAmount;
        if (remainingToGather > 0)
        {
            var job = new ObtainItem(
                Character,
                gameState,
                Character.Schema.Task,
                amount - progressAmount
            );

            job.CanTriggerTraining = CanTriggerTraining;

            jobs.Add(job);
        }

        var completeTask = new CompleteTask(
            Character,
            gameState,
            ItemCode,
            ItemAmount
        ).SetParent<CompleteTask>(this);

        jobs.Add(completeTask);

        completeTask.onSuccessEndHook = onSuccessEndHook;

        Character.QueueJobsAfter(Id, jobs);

        // Reset it
        onSuccessEndHook = null;

        logger.LogInformation(
            $"{JobName}: [{Character.Schema.Name}] - found {jobs.Count} jobs to run, to complete task {Code}"
        );

        return Task.FromResult<OneOf<AppError, None>>(new None());
    }
}
