using Application.ArtifactsApi.Schemas;
using Application.Character;
using Application.Dtos;
using Application.Errors;
using Applicaton.Services.FightSimulator;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public class MonsterTask : CharacterJob
{
    public string? ItemCode { get; set; }
    public int? ItemAmount { get; set; }

    public MonsterTask(
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

            var taskCoinsAmount = Character.GetItemFromInventory("tasks_coin")?.Quantity ?? 0;

            if (taskCoinsAmount > 0)
            {
                logger.LogInformation(
                    $"{JobName}: [{Character.Schema.Name}] onSuccessHook: found {taskCoinsAmount} task coins - queue depositting them"
                );
                Character.QueueJob(
                    new DepositItems(
                        Character,
                        gameState,
                        "tasks_coin",
                        taskCoinsAmount
                    ).SetParent<DepositItems>(this),
                    true
                );
            }

            if (ItemCode is not null && ItemAmount is not null)
            {
                logger.LogInformation(
                    $"{JobName}: [{Character.Schema.Name}] onSuccessHook: found {ItemAmount} x {ItemCode} - queue depositting them"
                );
                Character.QueueJob(
                    new DepositItems(
                        Character,
                        gameState,
                        ItemCode,
                        (int)ItemAmount
                    ).SetParent<DepositItems>(this),
                    true
                );
            }

            return Task.Run(() => { });
        };
    }

    protected override Task<OneOf<AppError, None>> ExecuteAsync()
    {
        logger.LogInformation($"{JobName} run started - for {Character.Schema.Name}");

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
                        TaskType.monsters
                    ).SetParent<AcceptNewTask>(this),
                ]
            );
            Status = JobStatus.Suspend;
            return Task.FromResult<OneOf<AppError, None>>(new None());
        }

        if (Character.Schema.TaskType == TaskType.monsters.ToString())
        {
            var code = Character.Schema.Task;
            MonsterSchema? monster = gameState.Monsters.FirstOrDefault(monster =>
                monster.Code == code!
            );
            if (monster is null)
            {
                Status = JobStatus.Failed;
                return Task.FromResult<OneOf<AppError, None>>(
                    new AppError($"Cannot find monster {code} to fight in task")
                );
            }
            var outcome = FightSimulator.CalculateFightOutcomeWithBestEquipment(
                Character,
                monster,
                gameState
            );

            if (!outcome.ShouldFight)
            {
                return Task.FromResult<OneOf<AppError, None>>(
                    new AppError(
                        $"Cannot complete monster task, because the monster is too strong - outcome: {outcome.ShouldFight} - remaining monster hp: {outcome.MonsterHp} - monster {code} to fight in task"
                    )
                );
            }
        }
        else
        {
            return Task.FromResult<OneOf<AppError, None>>(
                new AppError(
                    $"Cannot do a {JobName}, because the current task is {Character.Schema.TaskType}"
                )
            );
        }

        int progressAmount = Character.Schema.TaskProgress;
        int amount = Character.Schema.TaskTotal;

        int remainingToKill = amount - progressAmount;
        if (remainingToKill > 0)
        {
            jobs.Add(
                new FightMonster(
                    Character,
                    gameState,
                    Character.Schema.Task,
                    amount - progressAmount
                ).SetParent<FightMonster>(this)
            );
        }

        jobs.Add(
            new CompleteTask(Character, gameState, ItemCode, ItemAmount).SetParent<CompleteTask>(
                this
            )
        );

        if (jobs.Count > 0)
        {
            jobs.Last()!.onSuccessEndHook = onSuccessEndHook;

            Character.QueueJobsAfter(Id, jobs);
        }

        // Reset it
        onSuccessEndHook = () => Task.Run(() => { });

        logger.LogInformation(
            $"{JobName}: [{Character.Schema.Name}] - found {jobs.Count} jobs to run, to complete task {Code}"
        );

        return Task.FromResult<OneOf<AppError, None>>(new None());
    }
}
