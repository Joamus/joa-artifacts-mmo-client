using Application.ArtifactsApi.Schemas;
using Application.ArtifactsApi.Schemas.Requests;
using Application.Character;
using Application.Dtos;
using Application.Errors;
using Application.Services;
using Microsoft.OpenApi.Extensions;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public class CancelTaskJob : CharacterJob
{
    public CancelTaskJob(PlayerCharacter playerCharacter, GameState gameState)
        : base(playerCharacter, gameState) { }

    protected override async Task<OneOf<AppError, None>> ExecuteAsync()
    {
        return await DoCancelTask(Character, gameState);
    }

    public static async Task<OneOf<AppError, None>> DoCancelTask(
        PlayerCharacter character,
        GameState gameState
    )
    {
        if (string.IsNullOrWhiteSpace(character.Schema.Task))
        {
            return new None();
        }

        bool canCancelTask = false;

        int tasksCoinsInInventory =
            character.GetItemFromInventory(ItemService.TasksCoin)?.Quantity ?? 0;

        if (tasksCoinsInInventory >= ItemService.CancelTaskPrice)
        {
            canCancelTask = true;
        }
        else if (tasksCoinsInInventory < ItemService.CancelTaskPrice)
        {
            int tasksCoinsInBank =
                (await gameState.BankItemCache.GetBankItems(character))
                    .Data.FirstOrDefault(item => item.Code == ItemService.TasksCoin)
                    ?.Quantity ?? 0;

            if (tasksCoinsInBank >= ItemService.CancelTaskPrice)
            {
                await character.NavigateTo("bank");

                await character.WithdrawBankItem(
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
                $"Cannot cancel current task of type \"{character.Schema.TaskType}\", because the character doesn't have enough tasks coins"
            );
        }

        string tasksMasterCode = (
            character.Schema.TaskType == TaskType.items.GetDisplayName()
                ? TaskType.items
                : TaskType.monsters
        ).ToString();

        await character.NavigateTo(tasksMasterCode);

        await character.TaskCancel();

        return new None();
    }

    public static async Task<bool> CanCancelTask(PlayerCharacter character, GameState gameState)
    {
        int tasksCoinsInInventory =
            character.GetItemFromInventory(ItemService.TasksCoin)?.Quantity ?? 0;

        if (tasksCoinsInInventory >= ItemService.CancelTaskPrice)
        {
            return true;
        }

        int tasksCoinsInBank =
            (await gameState.BankItemCache.GetBankItems(character))
                .Data.FirstOrDefault(item => item.Code == ItemService.TasksCoin)
                ?.Quantity ?? 0;

        return tasksCoinsInInventory + tasksCoinsInBank > ItemService.CancelTaskPrice;
    }

    public static async Task<bool> ShouldCancelTask(GameState gameState, ItemSchema item)
    {
        /**
        ** If the item is an event item, e.g. strange_ore, we want to cancel it if we have the tasks coins for it.
        ** In the worst case scenario, we might not have the coins, but let's fake not being able to do it.
        */
        return gameState.EventService.IsEntityFromEvent(item.Code)
            || item.Craft is not null
                && item.Craft.Items.Exists(itemComponent =>
                {
                    var matchingItemComponent = gameState.ItemsDict[itemComponent.Code];

                    return gameState.EventService.IsEntityFromEvent(matchingItemComponent.Code);
                });
    }
}
