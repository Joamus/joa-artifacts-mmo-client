using Application.Character;
using Application.Errors;
using Application.Services;
using Application.Services.ApiServices;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public class BuyItemNpc : CharacterJob
{
    public bool AllowObtainingCurrency { get; set; } = false;
    public bool UseInventory { get; set; } = false;
    public bool UseBank { get; set; } = false;
    readonly int amount;

    // public BuyItemNpc(PlayerCharacter playerCharacter, GameState gameState)
    //     : base(playerCharacter, gameState) { }

    public BuyItemNpc(
        PlayerCharacter playerCharacter,
        GameState gameState,
        string code,
        int amount,
        bool allowObtainingCurrency,
        bool useInventory = true,
        bool useBank = true
    )
        : base(playerCharacter, gameState)
    {
        AllowObtainingCurrency = allowObtainingCurrency;
        Code = code;
        this.amount = amount;
    }

    protected override async Task<OneOf<AppError, None>> ExecuteAsync()
    {
        List<CharacterJob> jobs = [];

        var itemToBuy = gameState.NpcItemsDict.GetValueOrNull(Code!);

        if (itemToBuy is null)
        {
            return new AppError($"Could not find item \"{Code}\" in NpcItemsDict");
        }

        var matchingCurrency = gameState.ItemsDict.GetValueOrNull(itemToBuy.Currency);

        if (matchingCurrency is null)
        {
            return new AppError(
                $"Could not find matching currency item \"{itemToBuy.Currency}\" in items dict"
            );
        }

        bool isGold = itemToBuy.Currency == "gold";

        int amountLeft = amount * itemToBuy.BuyPrice ?? 0;

        if (UseBank)
        {
            if (isGold)
            {
                var bankResponse = await gameState.AccountRequester.GetBankDetails();

                int amountNeeded = Math.Min(bankResponse.Data.Gold, amountLeft);
                amountLeft -= amountNeeded;

                jobs.Add(new WithdrawGold(Character, gameState, amountNeeded));
            }
            else
            {
                var bankResponse = await gameState.AccountRequester.GetBankItems();

                var itemInBank = bankResponse.Data.Find(item => item.Code == matchingCurrency.Code);

                if (itemInBank is not null)
                {
                    int amountNeeded = Math.Min(itemInBank.Quantity, amountLeft);
                    amountLeft -= amountNeeded;

                    jobs.Add(new WithdrawItem(Character, gameState, itemInBank.Code, amountNeeded));
                }
            }
        }

        if (amountLeft > 0 && UseInventory && !isGold)
        {
            var itemInInventory = Character.GetItemFromInventory(matchingCurrency.Code);

            if (itemInInventory is not null)
            {
                int amountNeeded = Math.Min(itemInInventory.Quantity, amountLeft);
                amountLeft -= amountNeeded;

                jobs.Add(
                    new WithdrawItem(Character, gameState, itemInInventory.Code, amountNeeded)
                );
            }
        }

        // Only do this if we still need materials.
        if (amountLeft > 0 && AllowObtainingCurrency)
        {
            if (itemToBuy.Currency == "gold")
            {
                // TODO: grind gold - maybe find best monster to kill according to level?
            }
            else
            {
                jobs.Add(new ObtainItem(Character, gameState, matchingCurrency.Code, amountLeft));

                Character.QueueJobsBefore(Id, jobs);
                Status = JobStatus.Suspend;
                return new None();
            }
        }

        if (amountLeft > 0)
        {
            // handle that we cannot get all of it?
        }

        return new None();
    }
}
