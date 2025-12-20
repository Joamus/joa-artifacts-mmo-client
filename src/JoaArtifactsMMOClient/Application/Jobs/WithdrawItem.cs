using Application.ArtifactsApi.Schemas.Requests;
using Application.ArtifactsApi.Schemas.Responses;
using Application.Character;
using Application.Errors;
using Applicaton.Jobs;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

/*
 * Looks in the bank, or possibly has another player deliver item to bank maybe?
*/
public class WithdrawItem : CharacterJob
{
    public bool CanTriggerObtain { get; set; }

    public WithdrawItem(
        PlayerCharacter character,
        GameState gameState,
        string code,
        int amount,
        bool canTriggerObtain = true
    // bool shouldReserve = true
    )
        : base(character, gameState)
    {
        Code = code;
        Amount = amount;
        CanTriggerObtain = canTriggerObtain;

        onJobQueuedHook = () =>
        {
            gameState.BankItemCache.ReserveItem(character, code, amount);

            return Task.Run(() => { });
        };
    }

    protected override async Task<OneOf<AppError, None>> ExecuteAsync()
    {
        var result = await gameState.BankItemCache.GetBankItems(Character);

        if (result is not BankItemsResponse bankItemsResponse)
        {
            return new AppError("Failed to get bank items");
        }

        var matchingItemInBank = bankItemsResponse.Data.FirstOrDefault(item => item.Code == Code);

        int foundQuantity = 0;

        if (matchingItemInBank is not null)
        {
            foundQuantity = Math.Min(Amount, matchingItemInBank.Quantity);
        }

        if (DepositUnneededItems.ShouldInitDepositItems(Character, false))
        {
            Character.QueueJobsBefore(Id, [new DepositUnneededItems(Character, gameState)]);
            Status = JobStatus.Suspend;
            return new None();
        }

        if (
            Character.GetInventorySpaceLeft() <= foundQuantity
            || Character.Schema.Inventory.Count(item => string.IsNullOrWhiteSpace(item.Code)) < 1
        )
        {
            Character.QueueJobsBefore(Id, [new DepositUnneededItems(Character, gameState)]);
            Status = JobStatus.Suspend;
            return new None();
        }

        if (foundQuantity > 0)
        {
            await Character.NavigateTo("bank");
            var withdrawResult = await Character.WithdrawBankItem(
                [new WithdrawOrDepositItemRequest { Code = Code!, Quantity = foundQuantity }]
            );
            // There can be a clash
            if (withdrawResult.Value is None)
            {
                gameState.BankItemCache.RemoveReservation(Character, Code, foundQuantity);
                return new None();
            }
        }

        if (CanTriggerObtain)
        {
            logger.LogWarning(
                $"{JobName}: [{Character.Schema.Name}]: Triggering obtain - found quantity of {Code} was {foundQuantity}"
            );
            var job = new ObtainOrFindItem(Character, gameState, Code, Amount);
            job.AllowUsingMaterialsFromBank = true;

            Character.QueueJobsAfter(Id, [job]);
            return new None();
        }
        else
        {
            return new AppError("No items found", ErrorStatus.NotFound);
        }
    }
}
