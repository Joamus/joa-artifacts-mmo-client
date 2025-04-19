using Application;
using Application.ArtifactsApi.Schemas;
using Application.ArtifactsApi.Schemas.Responses;
using Application.Character;
using Application.Errors;
using Application.Jobs;
using Application.Services;
using Application.Services.ApiServices;
using OneOf;
using OneOf.Types;

namespace Applicaton.Jobs;

public class DepositUnneededItems : CharacterJob
{
    public DepositUnneededItems(PlayerCharacter playerCharacter)
        : base(playerCharacter) { }

    private static readonly List<string> _equipmentTypes =
    [
        "weapon",
        "shield",
        "body_armor",
        "leg_armor",
        "ring",
        "amulet",
        "artifact",
        "rune",
        "bag",
        "utility",
    ];

    // Deposit until hitting this threshold
    private static double MIN_FREE_INVENTORY_SPACES = 5;
    private static double MAX_FREE_INVENTORY_SPACES = 30;

    public override async Task<OneOf<JobError, None>> RunAsync()
    {
        _logger.LogInformation(
            $"{GetType().Name} run started for {_playerCharacter._character.Name}"
        );

        List<(string Code, int Quantity, ItemImportance Importance)> itemsToDeposit = [];

        // Deposit least important items

        var accountRequester = GameServiceProvider.GetInstance().GetService<AccountRequester>()!;

        var result = await accountRequester.GetBankItems();

        if (result is not BankItemsResponse bankItemsResponse)
        {
            return new JobError("Failed to get bank items");
        }

        Dictionary<string, int> bankItems = new();

        foreach (var item in bankItemsResponse.Data)
        {
            bankItems.Add(item.Code, item.Quantity);
        }

        // TODO: NICE TO HAVE would be to find out if the item can be crafted into something that is already in the bank,
        // e.g the character has raw chicken, but there is cooked chicken in the bank. They could then run over to the cooking station,
        // cook the chicken, and then come back.

        foreach (var item in _playerCharacter._character.Inventory)
        {
            if (item.Code == "")
            {
                continue;
            }
            bool itemIsUsedForTask = item.Code == _playerCharacter._character.Task;

            if (itemIsUsedForTask)
            {
                itemsToDeposit.Add((item.Code, item.Quantity, Importance: ItemImportance.High));
                continue;
            }

            ItemSchema matchingItem = _gameState.Items.FirstOrDefault(_item =>
                _item.Code == item.Code
            )!;

            if (_equipmentTypes.Contains(matchingItem.Type))
            {
                itemsToDeposit.Add((item.Code, item.Quantity, Importance: ItemImportance.Medium));
                continue;
            }

            if (
                matchingItem.Subtype == "food"
                && _playerCharacter._character.Level >= matchingItem.Level
                && (_playerCharacter._character.Level - matchingItem.Level)
                    <= PlayerCharacter.PREFERED_FOOD_LEVEL_DIFFERENCE
            )
            {
                int amountToKeep = Math.Min(PlayerCharacter.AMOUNT_OF_FOOD_TO_KEEP, item.Quantity);

                int amountToDeposit = item.Quantity - amountToKeep;

                if (amountToDeposit > 0)
                {
                    itemsToDeposit.Add(
                        (item.Code, item.Quantity, Importance: ItemImportance.Medium)
                    );
                }
                continue;
            }

            if (_playerCharacter._jobs.Find(job => job._code == item.Code) is not null)
            {
                itemsToDeposit.Add((item.Code, item.Quantity, Importance: ItemImportance.High));
                continue;
            }

            var quantityInBank = bankItems.ContainsKey(item.Code) ? bankItems[item.Code] : 0;
            // We can store like 9+ billion items in the bank, so no reason to check if we are gonna cap by storing more items.
            // We want to prioritize storing items in the bank that we already have in the bank.

            var importance = quantityInBank > 0 ? ItemImportance.None : ItemImportance.Low;

            itemsToDeposit.Add((item.Code, item.Quantity, Importance: importance));
        }

        itemsToDeposit.Sort((a, b) => a.Importance.CompareTo(b.Importance));

        await _playerCharacter.NavigateTo("bank", ContentType.Bank);

        foreach (var item in itemsToDeposit)
        {
            if (!ShouldKeepDepositingIfAtBank(_playerCharacter))
            {
                break;
            }

            await _playerCharacter.DepositBankItem(item.Code, item.Quantity);
        }

        if (_playerCharacter.GetInventorySpaceLeft() >= MIN_FREE_INVENTORY_SPACES)
        {
            // TODO: Handle that we cannot tidy up enough - maybe spawn a HouseKeeping job? It would cook and craft items in the bank,
            // which often ends up taking up less space
        }

        _logger.LogInformation(
            $"{GetType().Name} completed for {_playerCharacter._character.Name}"
        );

        return new None();
    }

    public static bool ShouldInitDepositItems(PlayerCharacter character)
    {
        return character.GetInventorySpaceLeft() < MIN_FREE_INVENTORY_SPACES;
    }

    public static bool ShouldKeepDepositingIfAtBank(PlayerCharacter character)
    {
        return character.GetInventorySpaceLeft() < MAX_FREE_INVENTORY_SPACES;
    }
}

enum ItemImportance
{
    None = 0,
    Low = 10,
    Medium = 20,
    High = 30,
}
