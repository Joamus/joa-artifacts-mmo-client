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

    protected override Task<OneOf<AppError, None>> ExecuteAsync()
    {
        logger.LogInformation($"{GetType().Name}: [{Character.Schema.Name}] run started");

        List<CharacterJob> jobs = [];

        if (Character.Schema.TaskType == "")
        {
            // Go pick up task - then we should continue
            Character.QueueJobsBefore(
                Id,
                [new AcceptNewTask(Character, gameState, TaskType.items)]
            );
            return Task.FromResult<OneOf<AppError, None>>(new None());
        }

        // For gather tasks, we are only going to get tasks that we have high enough skill to do, which means that it shouldn't be needed
        // to check if we have enough skill etc. to complete the task
        if (Character.Schema.TaskType != TaskType.items.ToString())
        {
            return Task.FromResult<OneOf<AppError, None>>(
                new AppError(
                    $"Cannot do a {GetType().Name}, because the current task is {Character.Schema.TaskType}"
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

            jobs.Add(job);
        }

        jobs.Add(new CompleteTask(Character, gameState, ItemCode, ItemAmount));

        Character.QueueJobsAfter(Id, jobs);

        logger.LogInformation(
            $"{GetType().Name}: [{Character.Schema.Name}] - found {jobs.Count} jobs to run, to complete task {Code}"
        );

        return Task.FromResult<OneOf<AppError, None>>(new None());
    }
}
