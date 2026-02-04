using Application.Character;
using Application.Errors;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public class BuyItemNpc : CharacterJob
{
    public bool AllowObtainingCurrency { get; set; } = true;
    public bool UseInventory { get; set; } = true;
    public bool UseBank { get; set; } = true;

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
        Amount = amount;
        UseInventory = useInventory;
        UseBank = useBank;
    }

    protected override async Task<OneOf<AppError, None>> ExecuteAsync()
    {
        List<CharacterJob> jobs = [];

        var itemToBuy = gameState.NpcItemsDict.GetValueOrNull(Code!);

        if (itemToBuy is null)
        {
            return new AppError($"Could not find item \"{Code}\" in NpcItemsDict");
        }

        bool isGold = itemToBuy.Currency == "gold";

        int amountLeft = Amount * itemToBuy.BuyPrice ?? 0;

        var matchingCurrency = gameState.ItemsDict.GetValueOrNull(itemToBuy.Currency);

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
                if (matchingCurrency is null)
                {
                    return new AppError(
                        $"Could not find matching currency item \"{itemToBuy.Currency}\" in items dict"
                    );
                }
                var bankResponse = await gameState.BankItemCache.GetBankItems(Character);

                var itemInBank = bankResponse.Data.Find(item => item.Code == matchingCurrency.Code);

                if (itemInBank is not null)
                {
                    int amountNeeded = Math.Min(itemInBank.Quantity, amountLeft);
                    amountLeft -= amountNeeded;

                    jobs.Add(new WithdrawItem(Character, gameState, itemInBank.Code, amountNeeded));
                }
            }
        }

        if (amountLeft > 0 && UseInventory)
        {
            if (isGold)
            {
                amountLeft -= Character.Schema.Gold;
            }
            else
            {
                var itemInInventory = Character.GetItemFromInventory(matchingCurrency!.Code);

                if (itemInInventory is not null)
                {
                    int amountNeeded = Math.Min(itemInInventory.Quantity, amountLeft);
                    amountLeft -= amountNeeded;
                }
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
                jobs.Add(
                    new ObtainOrFindItem(Character, gameState, matchingCurrency.Code, amountLeft)
                );

                await Character.QueueJobsBefore(Id, jobs);
                Status = JobStatus.Suspend;
                return new None();
            }
        }

        if (amountLeft > 0)
        {
            return new AppError(
                $"{JobName}: [{Character.Schema.Name}] Still have {amountLeft} x {matchingCurrency.Code} left to find when buying item"
            );
        }
        await Character.NavigateTo(itemToBuy.Npc);
        await Character.NpcBuyItem(itemToBuy.Code, Amount);

        return new None();
    }
}
