using Application;
using Application.ArtifactsApi.Schemas;
using Application.ArtifactsApi.Schemas.Responses;
using Application.Character;
using Application.Services.ApiServices;

public class BankItemCache
{
    private readonly SemaphoreSlim LoadItemsLock = new(1, 1);
    public bool shouldRequestAgain { get; set; } = true;
    public bool shouldRequestDetailsAgain { get; set; } = true;

    BankItemsResponse? lastResponse { get; set; } = null;
    BankDetailsResponse? lastDetailsResponse { get; set; } = null;

    AccountRequester accountRequester { get; init; }

    DateTime lastCleanUpAt { get; set; } = DateTime.UtcNow;

    const int CLEAN_UP_MINUTE_INTERVAL = 30;

    public BankItemCache(AccountRequester accountRequester)
    {
        this.accountRequester = accountRequester;
    }

    Dictionary<string, List<ItemReservation>> reservations { get; set; } = [];

    public async void ReserveItem(PlayerCharacter character, string code, int amount)
    {
        await PreRun();

        var existingReservations = reservations.GetValueOrNull(code);

        var reservation = new ItemReservation
        {
            Item = new DropSchema { Code = code, Quantity = amount },
            CharacterName = character.Schema.Name,
            CreatedAt = DateTime.UtcNow,
        };

        if (existingReservations is null)
        {
            reservations[code] = [reservation];
        }
        else
        {
            existingReservations.Add(
                new ItemReservation
                {
                    Item = new DropSchema { Code = code, Quantity = amount },
                    CharacterName = character.Schema.Name,
                    CreatedAt = DateTime.UtcNow,
                }
            );
        }
    }

    public async void RemoveReservation(PlayerCharacter character, string code, int amount)
    {
        await PreRun();

        var existingReservations = reservations.GetValueOrNull(code);

        if (existingReservations is not null)
        {
            int amountLeftToSubtract = amount;

            foreach (var existingReservation in existingReservations)
            {
                if (existingReservation.CharacterName != character.Schema.Name)
                {
                    continue;
                }

                if (amountLeftToSubtract >= existingReservation.Item.Quantity)
                {
                    existingReservation.Item.Quantity = 0;
                    amountLeftToSubtract -= existingReservation.Item.Quantity;
                }
                else
                {
                    existingReservation.Item.Quantity -= amountLeftToSubtract;
                    amountLeftToSubtract = 0;
                }

                if (amountLeftToSubtract == 0)
                {
                    break;
                }
            }
        }

        RemoveEmptyReservations();
    }

    public async Task PreRun()
    {
        if (lastCleanUpAt <= DateTime.UtcNow.AddMinutes(-CLEAN_UP_MINUTE_INTERVAL))
        {
            CleanupOldReservations();
            // Just for good measure
            shouldRequestAgain = true;
            shouldRequestDetailsAgain = true;
        }
    }

    public void CleanupOldReservations()
    {
        Dictionary<string, List<ItemReservation>> newReservations = reservations
            .Select(reservation =>
            {
                KeyValuePair<string, List<ItemReservation>> result = new KeyValuePair<
                    string,
                    List<ItemReservation>
                >(
                    reservation.Key,
                    [
                        .. reservation.Value.Where(reservation =>
                            reservation.CreatedAt
                            > DateTime.Now.AddMinutes(-CLEAN_UP_MINUTE_INTERVAL)
                        ),
                    ]
                );

                return result;
            })
            .ToDictionary();

        reservations = newReservations;
    }

    public async Task<List<DropSchema>> GetBankItems(
        PlayerCharacter? playerCharacter,
        bool hideOwnReservations = false
    )
    {
        await PreRun();
        // Apply all reservations to the bank items
        // Maybe lazy cleanup the cache? Do it on an interval of every 30 min or so
        // Allow boolean parameter to get all anyway

        BankItemsResponse bankItemsResponse = await LazyGetBankItems();

        List<DropSchema> items =
        [
            .. bankItemsResponse
                .Data.Select(item =>
                {
                    int quantity = item.Quantity;

                    var reservationsForItem = reservations.GetValueOrNull(item.Code);

                    if (reservationsForItem is not null)
                    {
                        foreach (var reservation in reservationsForItem)
                        {
                            if (
                                playerCharacter is not null
                                && reservation.CharacterName == playerCharacter.Schema.Name
                                && !hideOwnReservations
                            )
                            {
                                continue;
                            }
                            quantity -= reservation.Item.Quantity;

                            if (quantity < 0)
                            {
                                quantity = 0;
                                break;
                            }
                        }
                    }

                    return item with
                    {
                        Code = item.Code,
                        Quantity = quantity,
                    };
                })
                .Where(item => item.Quantity > 0),
        ];

        return items;
    }

    async Task<BankItemsResponse> LazyGetBankItems()
    {
        await LoadItemsLock.WaitAsync();

        if (!shouldRequestAgain && lastResponse is not null)
        {
            LoadItemsLock.Release();
            return lastResponse;
        }

        BankItemsResponse? bankResponseResult = null;

        try
        {
            BankItemsResponse bankResponse = await accountRequester.GetBankItems();

            bankResponseResult = bankResponse;

            lastResponse = bankResponse;
        }
        finally
        {
            LoadItemsLock.Release();
        }

        shouldRequestAgain = false;

        return bankResponseResult;
    }

    public async Task<BankDetails> GetBankDetails()
    {
        await PreRun();

        bool requestAgain = lastDetailsResponse is null || shouldRequestDetailsAgain;

        BankDetailsResponse bankDetails = requestAgain
            ? await accountRequester.GetBankDetails()
            : lastDetailsResponse! with
            { }; // dunno if the cloning really works here, or is necessary

        if (requestAgain)
        {
            lastDetailsResponse = bankDetails with { };
        }

        shouldRequestDetailsAgain = false;

        return bankDetails.Data;
    }

    private void RemoveEmptyReservations()
    {
        List<string> keysToRemove = [];
        foreach (var reservation in reservations)
        {
            bool isEmpty = true;
            foreach (var list in reservation.Value)
            {
                if (list.Item.Quantity > 0)
                {
                    isEmpty = false;
                    break;
                }
            }

            reservation.Value.RemoveAll(list => list.Item.Quantity < 0);

            if (isEmpty)
            {
                keysToRemove.Add(reservation.Key);
            }
        }

        foreach (var key in keysToRemove)
        {
            reservations.Remove(key);
        }
    }

    public async Task<int> GetTotalBudgetInBank()
    {
        var bankDetails = await GetBankDetails();

        return GetTotalBudgetFormula(bankDetails.Gold, bankDetails.NextExpansionCost);
    }

    static int GetTotalBudgetFormula(int goldInBank, int nextExpansionCost)
    {
        if (nextExpansionCost >= goldInBank)
        {
            return 0;
        }

        /**
        ** As long as we can afford the next bank expansion, we can spend whatever gold we want.
        ** We could potentially make this more advanced, e.g. if we have 60% of the gold needed for the next expansion,
        ** then our characters can spend everything above that.
        */
        return goldInBank - nextExpansionCost;
    }
}

public record ItemReservation
{
    public required DropSchema Item { get; set; }
    public required string CharacterName { get; set; }

    public required DateTime CreatedAt { get; set; }
}
