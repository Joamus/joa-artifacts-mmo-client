using Application.ArtifactsApi.Schemas.Requests;
using Application.ArtifactsApi.Schemas.Responses;
using Application.Character;
using Application.Errors;
using Application.Services;
using Application.Services.ApiServices;
using Applicaton.Jobs;
using Microsoft.AspNetCore.SignalR;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

/*
 * Looks in the bank, or possibly has another player deliver item to bank maybe?
*/
public class WithdrawItem : CharacterJob
{
    public bool CanTriggerObtain { get; set; }
    private int _amount { get; set; }

    public WithdrawItem(
        PlayerCharacter character,
        GameState gameState,
        string code,
        int amount,
        bool canTriggerObtain = true
    )
        : base(character, gameState)
    {
        Code = code;
        _amount = amount;
        CanTriggerObtain = canTriggerObtain;
    }

    protected override async Task<OneOf<AppError, None>> ExecuteAsync()
    {
        var accountRequester = GameServiceProvider.GetInstance().GetService<AccountRequester>()!;

        var result = await accountRequester.GetBankItems();

        if (result is not BankItemsResponse bankItemsResponse)
        {
            return new AppError("Failed to get bank items");
        }

        var matchingItemInBank = bankItemsResponse.Data.FirstOrDefault(item => item.Code == Code);

        int foundQuantity = 0;

        if (matchingItemInBank is not null)
        {
            foundQuantity = Math.Min(_amount, matchingItemInBank.Quantity);
        }

        if (Character.GetInventorySpaceLeft() < foundQuantity)
        {
            Character.QueueJobsBefore(Id, [new DepositUnneededItems(Character, gameState)]);
            Status = JobStatus.Suspend;
            return new None();
        }

        if (foundQuantity > 0)
        {
            await Character.NavigateTo("bank", ArtifactsApi.Schemas.ContentType.Bank);
            var withdrawResult = await Character.WithdrawBankItem(
                [new WithdrawOrDepositItemRequest { Code = Code!, Quantity = foundQuantity }]
            );
            // There can be a clash
            if (withdrawResult.Value is None)
            {
                return new None();
            }
        }

        if (CanTriggerObtain)
        {
            Character.QueueJobsAfter(Id, [new ObtainItem(Character, gameState, Code, _amount)]);
            return new None();
        }
        else
        {
            return new AppError("No items found", ErrorStatus.NotFound);
        }
    }
}
