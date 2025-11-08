using Application.Character;
using Application.Dtos;
using Application.Errors;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public class DoTaskUntilObtainedItem : CharacterJob
{
    public TaskType Type { get; private set; }

    public DoTaskUntilObtainedItem(
        PlayerCharacter playerCharacter,
        GameState gameState,
        TaskType type,
        string itemCode,
        int amount
    )
        : base(playerCharacter, gameState)
    {
        Type = type;
        Code = itemCode;
        Amount = amount;
    }

    protected override async Task<OneOf<AppError, None>> ExecuteAsync()
    {
        logger.LogInformation($"{JobName}: [{Character.Schema.Name}] run started");

        // This job essentially just keeps queueing Monster/Item task jobs before it self, and then checking to see if the character has obtained the goal or not.

        int amountInInventory = Character.GetItemFromInventory(Code)?.Quantity ?? 0;

        if (amountInInventory < Amount)
        {
            CharacterJob task =
                Code == TaskType.monsters.ToString()
                || Character.Schema.TaskType == TaskType.monsters.ToString()
                    ? new MonsterTask(Character, gameState, Code, Amount)
                    : new ItemTask(Character, gameState, Code, Amount);

            logger.LogInformation(
                $"{JobName}: [{Character.Schema.Name}] queueing another task - have {amountInInventory}/{Amount} currently"
            );
            Character.QueueJobsBefore(Id, [task]);
            Status = JobStatus.Suspend;
            return new None();
        }

        logger.LogInformation(
            $"{JobName}: [{Character.Schema.Name}] completed - progress {Code} ({amountInInventory}/{Amount})"
        );

        return new None();
    }
}
