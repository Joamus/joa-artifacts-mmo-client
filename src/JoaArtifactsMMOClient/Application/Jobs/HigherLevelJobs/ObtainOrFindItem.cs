using Application.Character;
using Application.Errors;
using Application.Services;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public class ObtainOrFindItem : CharacterJob
{
    public bool AllowUsingMaterialsFromBank { get; set; } = true;

    public bool AllowUsingMaterialsFromInventory { get; set; } = true;

    public bool CanTriggerTraining { get; set; } = true;

    public ObtainOrFindItem(
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
        logger.LogInformation(
            $"{JobName}: [{Character.Schema.Name}] run started - progress {Code} ({Amount})"
        );

        List<CharacterJob> jobs = [];

        int amountLeft = Amount;

        var itemsInBank = await gameState.BankItemCache.GetBankItems(Character);

        var matchInBank = itemsInBank.Data.FirstOrDefault(item => item.Code == Code);

        if (matchInBank is not null)
        {
            int amountToTake = Math.Min(matchInBank.Quantity, amountLeft);

            jobs.Add(new WithdrawItem(Character, gameState, Code, amountToTake));

            amountLeft -= amountToTake;

            logger.LogInformation(
                $"{JobName}: [{Character.Schema.Name}] found {matchInBank.Quantity} x {Code} in bank - {amountLeft} to acquire"
            );
        }

        if (amountLeft > 0)
        {
            jobs.Add(
                ItemService.GetObtainOrCraftForJob(
                    Character,
                    gameState,
                    gameState.ItemsDict.GetValueOrNull(Code)!,
                    amountLeft
                )
            );
        }

        Character.QueueJobsAfter(Id, jobs);

        return new None();
    }
}
