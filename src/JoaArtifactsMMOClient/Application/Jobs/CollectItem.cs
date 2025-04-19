using Application.ArtifactsApi.Schemas.Responses;
using Application.Character;
using Application.Errors;
using Application.Services;
using Application.Services.ApiServices;
using Applicaton.Jobs;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

/*
 * Looks in the bank, or possibly has another player deliver item to bank maybe?
*/
public class CollectItem : CharacterJob
{
    private int _amount { get; set; }

    public CollectItem(PlayerCharacter character, string code, int amount)
        : base(character)
    {
        _code = code;
        _amount = amount;
    }

    public override async Task<OneOf<JobError, None>> RunAsync()
    {
        var accountRequester = GameServiceProvider.GetInstance().GetService<AccountRequester>()!;

        var result = await accountRequester.GetBankItems();

        if (result is not BankItemsResponse bankItemsResponse)
        {
            return new JobError("Failed to get bank items");
        }

        var matchingItemInBank = bankItemsResponse.Data.FirstOrDefault(item => item.Code == _code);

        int foundQuantity = 0;

        if (matchingItemInBank is not null)
        {
            foundQuantity = Math.Min(_amount, matchingItemInBank.Quantity);
        }

        if (_playerCharacter.GetInventorySpaceLeft() < foundQuantity)
        {
            _playerCharacter.QueueJobsBefore(Id, [new DepositUnneededItems(_playerCharacter)]);
            return new None();
        }

        if (foundQuantity > 0)
        {
            await _playerCharacter.NavigateTo("bank", ArtifactsApi.Schemas.ContentType.Bank);
            await _playerCharacter.WithdrawBankItem(_code, foundQuantity);
            return new None();
        }

        return new JobError("No items found", JobStatus.NotFound);
    }
}
