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

    protected override Task<OneOf<AppError, None>> ExecuteAsync()
    {
        logger.LogInformation($"{GetType().Name} run started - for {Character.Schema.Name}");

        List<CharacterJob> jobs = [];

        if (Character.Schema.TaskType == "")
        {
            // Go pick up task - then we should continue
            Character.QueueJobsBefore(
                Id,
                [new AcceptNewTask(Character, gameState, TaskType.monsters)]
            );
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
                return Task.FromResult<OneOf<AppError, None>>(
                    new AppError($"Cannot find monster {code} to fight in task")
                );
            }
            var outcome = FightSimulator.CalculateFightOutcome(Character.Schema, monster);

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
                    $"Cannot do a {GetType().Name}, because the current task is {Character.Schema.TaskType}"
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
                )
            );
        }

        jobs.Add(new CompleteTask(Character, gameState, ItemCode, ItemAmount));

        Character.QueueJobsAfter(Id, jobs);

        logger.LogInformation(
            $"{GetType().Name} - found {jobs.Count} jobs to run, to complete task {Code} for {Character.Schema.Name}"
        );

        return Task.FromResult<OneOf<AppError, None>>(new None());
    }
}
