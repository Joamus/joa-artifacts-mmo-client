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
    private readonly bool _canTriggerObtain = false;
    private int _amount { get; set; }

    public CollectItem(
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
        _canTriggerObtain = canTriggerObtain;
    }

    public override async Task<OneOf<AppError, None>> RunAsync()
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

        if (_playerCharacter.GetInventorySpaceLeft() < foundQuantity)
        {
            _playerCharacter.QueueJobsBefore(
                Id,
                [new DepositUnneededItems(_playerCharacter, _gameState)]
            );
            return new None();
        }

        if (foundQuantity > 0)
        {
            await _playerCharacter.NavigateTo("bank", ArtifactsApi.Schemas.ContentType.Bank);
            var withdrawResult = await _playerCharacter.WithdrawBankItem(Code, foundQuantity);
            // There can be a clash
            if (withdrawResult.Value is None _)
            {
                return new None();
            }
        }

        if (_canTriggerObtain)
        {
            _playerCharacter.QueueJobsAfter(
                Id,
                [new ObtainItem(_playerCharacter, _gameState, Code, _amount)]
            );
            return new None();
        }
        else
        {
            return new AppError("No items found", ErrorStatus.NotFound);
        }
    }
}
