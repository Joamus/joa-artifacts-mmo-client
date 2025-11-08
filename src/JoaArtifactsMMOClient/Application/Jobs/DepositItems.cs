using Application.ArtifactsApi.Schemas.Requests;
using Application.Character;
using Application.Errors;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public class DepositItems : CharacterJob
{
    public bool DontFailIfItemNotThere { get; set; } = false;

    public DepositItems(
        PlayerCharacter playerCharacter,
        GameState gameState,
        string code,
        int amount
    )
        : base(playerCharacter, gameState)
    {
        Code = code;
        Amount = amount;
    }

    protected override async Task<OneOf<AppError, None>> ExecuteAsync()
    {
        var amountInInventory = Character.GetItemFromInventory(Code)?.Quantity ?? 0;

        if (amountInInventory < Amount)
        {
            var errorMessage =
                $"{JobName}: [{Character.Schema.Name}]: Only found {amountInInventory} of {Amount} x {Code} in inventory";

            logger.LogWarning(errorMessage);

            if (amountInInventory == 0 && !DontFailIfItemNotThere)
            {
                return new AppError(
                    $"Could not deposit item(s) with code {Code} and amount {Amount} - could not find it in inventory"
                );
            }
        }

        await Character.NavigateTo("bank");

        // TODO: Handle that bank might be full

        if (amountInInventory > 0)
        {
            await Character.DepositBankItem(
                [
                    new WithdrawOrDepositItemRequest
                    {
                        Code = Code!,
                        Quantity = Math.Min(Amount, amountInInventory),
                    },
                ]
            );
        }
        else
        {
            logger.LogWarning(
                $"{JobName}: [{Character.Schema.Name}]: Nothing to deposit - skipping depositting (DontFailIfItemNotThere = {DontFailIfItemNotThere})"
            );
        }

        return new None();
    }
}
