using Application.ArtifactsApi.Schemas.Requests;
using Application.Character;
using Application.Dtos;
using Application.Errors;
using Application.Services;
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

        var bankResponse = await gameState.BankItemCache.GetBankItems(Character);

        int amountInBank =
            bankResponse.Data.FirstOrDefault(item => item.Code == Code)?.Quantity ?? 0;

        if (amountInBank > 0)
        {
            Character.QueueJobsBefore(
                Id,
                [new WithdrawItem(Character, gameState, Code, Math.Min(amountInBank, Amount))]
            );
            Status = JobStatus.Suspend;
            return new None();
        }

        if (Code != ItemService.TasksCoin)
        {
            int tasksCoinsInInventory =
                Character.GetItemFromInventory(ItemService.TasksCoin)?.Quantity ?? 0;

            int tasksCoinsInBank =
                bankResponse
                    .Data.FirstOrDefault(item => item.Code == ItemService.TasksCoin)
                    ?.Quantity ?? 0;

            int priceToBuyItem =
                (int)gameState.NpcItemsDict.GetValueOrNull(Code)!.BuyPrice! * Amount;

            if (tasksCoinsInInventory + tasksCoinsInBank >= priceToBuyItem)
            {
                if (tasksCoinsInInventory < priceToBuyItem)
                {
                    int amountNeededFromBank = priceToBuyItem - tasksCoinsInInventory;
                    await Character.NavigateTo("bank");

                    // Just to be careful
                    gameState.BankItemCache.ReserveItem(
                        Character,
                        ItemService.TasksCoin,
                        amountNeededFromBank
                    );

                    await Character.WithdrawBankItem(
                        [
                            new WithdrawOrDepositItemRequest
                            {
                                Code = ItemService.TasksCoin,
                                Quantity = amountNeededFromBank,
                            },
                        ]
                    );

                    gameState.BankItemCache.RemoveReservation(
                        Character,
                        ItemService.TasksCoin,
                        amountNeededFromBank
                    );

                    await Character.NavigateTo("tasks_trader");
                    await Character.NpcBuyItem(Code, Amount);
                    logger.LogInformation(
                        $"{JobName}: [{Character.Schema.Name}] completed - found all of the tasks coins in inventory ({tasksCoinsInInventory}) and bank ({tasksCoinsInBank}), and bought {Amount} x {Code}"
                    );

                    return new None();
                }
            }
        }

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
