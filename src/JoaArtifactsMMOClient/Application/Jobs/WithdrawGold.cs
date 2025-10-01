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
public class WithdrawGold : CharacterJob
{
    private readonly bool _canTriggerObtain = false;
    private int _amount { get; set; }

    public WithdrawGold(PlayerCharacter character, GameState gameState, int amount)
        : base(character, gameState)
    {
        _amount = amount;
    }

    protected override async Task<OneOf<AppError, None>> ExecuteAsync()
    {
        var result = await gameState.AccountRequester.GetBankDetails();

        int goldInBank = result.Data.Gold;

        if (goldInBank > 0)
        {
            await Character.NavigateTo("bank", ArtifactsApi.Schemas.ContentType.Bank);

            await Character.WithdrawBankGold(goldInBank);
        }

        // TODO: Allow grinding for gold?
        return new None();

        // if (_canTriggerObtain)
        // {
        //     Character.QueueJobsAfter(Id, [new ObtainItem(Character, gameState, Code, _amount)]);
        //     return new None();
        // }
        // else
        // {
        //     return new AppError("No items found", ErrorStatus.NotFound);
        // }
    }
}
