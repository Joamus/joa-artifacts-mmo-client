using Application.ArtifactsApi.Schemas.Requests;
using Application.Character;
using Application.Dtos;
using Application.Errors;
using Application.Services;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public class CancelTask : CharacterJob
{
    public CancelTask(PlayerCharacter playerCharacter, GameState gameState)
        : base(playerCharacter, gameState) { }

    protected override async Task<OneOf<AppError, None>> ExecuteAsync()
    {
        if (string.IsNullOrWhiteSpace(Character.Schema.Task))
        {
            return new None();
        }

        bool canCancelTask = false;

        int tasksCoinsInInventory =
            Character.GetItemFromInventory(ItemService.TasksCoin)?.Quantity ?? 0;

        if (tasksCoinsInInventory >= ItemService.CancelTaskPrice)
        {
            canCancelTask = true;
        }
        else if (tasksCoinsInInventory < ItemService.CancelTaskPrice)
        {
            int tasksCoinsInBank =
                (await gameState.BankItemCache.GetBankItems(Character))
                    .Data.FirstOrDefault(item => item.Code == ItemService.TasksCoin)
                    ?.Quantity ?? 0;

            if (tasksCoinsInBank >= ItemService.CancelTaskPrice)
            {
                await Character.NavigateTo("bank");

                await Character.WithdrawBankItem(
                    new List<WithdrawOrDepositItemRequest>
                    {
                        new WithdrawOrDepositItemRequest
                        {
                            Code = ItemService.TasksCoin,
                            Quantity = ItemService.CancelTaskPrice - tasksCoinsInInventory,
                        },
                    }
                );

                canCancelTask = true;
            }
        }

        if (!canCancelTask)
        {
            return new AppError(
                $"Cannot cancel current task of type \"{Character.Schema.TaskType}\", because the character doesn't have enough tasks coins"
            );
        }

        string tasksMasterCode = (
            Character.Schema.Task == TaskType.items.ToString() ? TaskType.items : TaskType.monsters
        ).ToString();

        await Character.NavigateTo(tasksMasterCode);

        await Character.TaskCancel();

        return new None();
    }

    public static async Task<bool> CanCancelTask(PlayerCharacter Character, GameState gameState)
    {
        int tasksCoinsInInventory =
            Character.GetItemFromInventory(ItemService.TasksCoin)?.Quantity ?? 0;

        if (tasksCoinsInInventory >= ItemService.CancelTaskPrice)
        {
            return true;
        }

        int tasksCoinsInBank =
            (await gameState.BankItemCache.GetBankItems(Character))
                .Data.FirstOrDefault(item => item.Code == ItemService.TasksCoin)
                ?.Quantity ?? 0;

        return tasksCoinsInInventory + tasksCoinsInBank > ItemService.CancelTaskPrice;
    }
}
