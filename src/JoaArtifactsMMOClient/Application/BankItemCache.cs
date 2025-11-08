using System.Security.Cryptography.X509Certificates;
using Application;
using Application.ArtifactsApi.Schemas;
using Application.ArtifactsApi.Schemas.Responses;
using Application.Character;
using Application.Services.ApiServices;

public class BankItemCache
{
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
        PreRun();

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
        PreRun();

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

    public async void PreRun()
    {
        if (lastCleanUpAt <= DateTime.UtcNow.AddMinutes(-CLEAN_UP_MINUTE_INTERVAL))
        {
            CleanupOldReservations();
        }
    }

    public void CleanupOldReservations() { }

    public async Task<BankItemsResponse> GetBankItems(
        PlayerCharacter playerCharacter,
        bool hideOwnReservations = false
    )
    {
        PreRun();
        // Apply all reservations to the bank items
        // Maybe lazy cleanup the cache? Do it on an interval of every 30 min or so
        // Allow boolean parameter to get all anyway
        var bankItems = await accountRequester.GetBankItems();

        foreach (var item in bankItems.Data)
        {
            var reservationsInCache = reservations.GetValueOrNull(item.Code);

            if (reservationsInCache is not null)
            {
                foreach (var reservation in reservationsInCache)
                {
                    if (
                        reservation.CharacterName == playerCharacter.Schema.Name
                        && !hideOwnReservations
                    )
                    {
                        continue;
                    }
                    item.Quantity -= reservation.Item.Quantity;

                    if (item.Quantity < 0)
                    {
                        item.Quantity = 0;
                        break;
                    }
                }
            }
        }

        bankItems.Data = bankItems.Data.Where(item => item.Quantity > 0).ToList();

        return bankItems;
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
}

public record ItemReservation
{
    public required DropSchema Item { get; set; }
    public required string CharacterName { get; set; }

    public required DateTime CreatedAt { get; set; }
}
