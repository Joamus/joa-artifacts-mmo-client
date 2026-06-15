using Application.ArtifactsApi.Schemas.Requests;
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

        onJobQueuedHook = async () =>
        {
            gameState.BankItemCache.ReserveItem(character, code, amount);
        };
    }

    protected override async Task<OneOf<AppError, None>> ExecuteAsync()
    {
        var bankItems = await gameState.BankItemCache.GetBankItems(Character);

        var matchingItemInBank = bankItems.FirstOrDefault(item => item.Code == Code);

        int foundQuantity = 0;

        if (matchingItemInBank is not null)
        {
            foundQuantity = Math.Min(Amount, matchingItemInBank.Quantity);
        }

        if (DepositUnneededItems.ShouldInitDepositItems(Character, false))
        {
            await Character.QueueJobsBefore(
                Id,
                [new DepositUnneededItems(Character, gameState, null, false)]
            );
            Status = JobStatus.Suspend;
            return new None();
        }

        if (
            Character.GetAvailableInventorySpace() <= foundQuantity
            || Character.Schema.Inventory.Count(item => string.IsNullOrWhiteSpace(item.Code)) < 1
        )
        {
            await Character.QueueJobsBefore(
                Id,
                [new DepositUnneededItems(Character, gameState, null, false)]
            );
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

            await Character.QueueJobsAfter(Id, [job]);
            return new None();
        }
        else
        {
            return new AppError("No items found", ErrorStatus.NotFound);
        }
    }
}
