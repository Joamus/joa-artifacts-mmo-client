using Application.ArtifactsApi.Schemas.Requests;
using Application.Character;
using Application.Errors;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public class DepositItems : CharacterJob
{
    public int _amount { get; init; }

    public DepositItems(
        PlayerCharacter playerCharacter,
        GameState gameState,
        string code,
        int amount
    )
        : base(playerCharacter, gameState)
    {
        Code = code;
        _amount = amount;
    }

    protected override async Task<OneOf<AppError, None>> ExecuteAsync()
    {
        var itemInInventory = Character.Schema.Inventory.Find(item => item.Code == Code);

        if (itemInInventory is null)
        {
            return new AppError(
                $"Could not deposit item(s) with code {Code} and amount ${_amount} - could not find it in inventory"
            );
        }

        await Character.NavigateTo("bank", ArtifactsApi.Schemas.ContentType.Bank);

        // TODO: Handle that bank might be full
        await Character.DepositBankItem(
            [
                new WithdrawOrDepositItemRequest
                {
                    Code = Code!,
                    Quantity = Math.Min(_amount, itemInInventory.Quantity),
                },
            ]
        );

        return new None();
    }
}
