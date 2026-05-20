using Application.Character;
using Application.Errors;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

/*
 * Looks in the bank, or possibly has another player deliver item to bank maybe?
*/
public class DepositGold : CharacterJob
{
    public DepositGold(PlayerCharacter character, GameState gameState, int amount)
        : base(character, gameState)
    {
        Amount = amount;
    }

    protected override async Task<OneOf<AppError, None>> ExecuteAsync()
    {
        await Character.NavigateTo("bank");

        await Character.DepositBankGold(Amount);

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
