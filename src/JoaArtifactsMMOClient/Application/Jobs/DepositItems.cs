using Application.Character;
using Application.Errors;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public class DepositItems : CharacterJob
{
    public required string _code { get; init; }
    public required int _amount { get; init; }

    public DepositItems(
        PlayerCharacter playerCharacter,
        GameState gameState,
        string code,
        int amount
    )
        : base(playerCharacter)
    {
        _code = code;
        _amount = amount;
    }

    public override async Task<OneOf<JobError, None>> RunAsync()
    {
        var itemInInventory = _playerCharacter._character.Inventory.Find(item =>
            item.Code == _code
        );

        if (itemInInventory is null)
        {
            return new JobError(
                $"Could not deposit item(s) with code {_code} and amount ${_amount} - could not find it in inventory"
            );
        }

        await _playerCharacter.NavigateTo("bank", ArtifactsApi.Schemas.ContentType.Bank);

        // TODO Handle that bank might be full
        await _playerCharacter.DepositBankItem(_code, Math.Min(_amount, itemInInventory.Quantity));

        return new None();
    }
}
