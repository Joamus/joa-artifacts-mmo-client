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

    public override async Task<OneOf<AppError, None>> RunAsync()
    {
        var itemInInventory = _playerCharacter.Character.Inventory.Find(item => item.Code == Code);

        if (itemInInventory is null)
        {
            return new AppError(
                $"Could not deposit item(s) with code {Code} and amount ${_amount} - could not find it in inventory"
            );
        }

        await _playerCharacter.NavigateTo("bank", ArtifactsApi.Schemas.ContentType.Bank);

        // TODO Handle that bank might be full
        await _playerCharacter.DepositBankItem(Code, Math.Min(_amount, itemInInventory.Quantity));

        return new None();
    }
}
