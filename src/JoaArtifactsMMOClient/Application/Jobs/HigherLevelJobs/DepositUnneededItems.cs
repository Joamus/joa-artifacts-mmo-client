using Application;
using Application.ArtifactsApi.Schemas;
using Application.ArtifactsApi.Schemas.Requests;
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
    public DepositUnneededItems(PlayerCharacter playerCharacter, GameState gameState)
        : base(playerCharacter, gameState) { }

    private static readonly List<string> _equipmentTypes =
    [
        "weapon",
        "shield",
        "helmet",
        "body_armor",
        "leg_armor",
        "boots",
        "ring",
        "amulet",
        "artifact",
        "rune",
        "bag",
        "utility",
    ];

    // Deposit until hitting this threshold
    private static int MIN_FREE_INVENTORY_SPACES = 5;
    private static int MAX_FREE_INVENTORY_SPACES = 30;

    protected override async Task<OneOf<AppError, None>> ExecuteAsync()
    {
        logger.LogInformation($"{JobName}: [{Character.Schema.Name}] run started");

        List<(string Code, int Quantity, ItemImportance Importance)> itemsToDeposit = [];
        (string Code, int Quantity)? itemToTurnIn = null;

        // Deposit least important items

        var accountRequester = gameState.AccountRequester;

        var result = await accountRequester.GetBankItems();

        if (result is not BankItemsResponse bankItemsResponse)
        {
            return new AppError("Failed to get bank items");
        }

        Dictionary<string, int> bankItems = new();

        foreach (var item in bankItemsResponse.Data)
        {
            bankItems.Add(item.Code, item.Quantity);
        }

        // TODO: NICE TO HAVE would be to find out if the item can be crafted into something that is already in the bank,
        // e.g the character has raw chicken, but there is cooked chicken in the bank. They could then run over to the cooking station,
        // cook the chicken, and then come back.

        foreach (var item in Character.Schema.Inventory)
        {
            if (item.Code == "")
            {
                continue;
            }
            bool itemIsUsedForTask = item.Code == Character.Schema.Task;

            if (itemIsUsedForTask)
            {
                // itemsToDeposit.Add((item.Code, item.Quantity, ItemImportance.High));
                itemToTurnIn = (item.Code, item.Quantity);
                continue;
            }

            ItemSchema matchingItem = gameState.Items.FirstOrDefault(_item =>
                _item.Code == item.Code
            )!;

            if (_equipmentTypes.Contains(matchingItem.Type))
            {
                var itemImportance = ItemImportance.Medium;

                if (matchingItem.Subtype == "tool")
                {
                    itemImportance = ItemImportance.High;
                }

                // TODO: Introduce logic to deposit equipment that isn't needed anymore

                itemsToDeposit.Add((item.Code, item.Quantity, Importance: itemImportance));
                continue;
            }

            if (
                matchingItem.Subtype == "food"
                && Character.Schema.Level >= matchingItem.Level
                && (Character.Schema.Level - matchingItem.Level)
                    <= PlayerCharacter.PREFERED_FOOD_LEVEL_DIFFERENCE
            )
            {
                int amountToKeep = Math.Min(
                    PlayerCharacter.MIN_AMOUNT_OF_FOOD_TO_KEEP,
                    item.Quantity
                );

                int amountToDeposit = item.Quantity - amountToKeep;

                if (amountToDeposit > 0)
                {
                    itemsToDeposit.Add(
                        (item.Code, item.Quantity, Importance: ItemImportance.Medium)
                    );
                }
                continue;
            }

            if (
                Character.Jobs.Find(job =>
                {
                    bool hasJobRelatedToIt = job.Code == item.Code;

                    if (hasJobRelatedToIt)
                    {
                        return hasJobRelatedToIt;
                    }

                    // Good enough, but it could technically go deeper - we might a job related to item C, but we have item A in our inventory
                    // and item A is an ingredient for item B, which is an ingredient for item C.
                    bool isIngredientOf =
                        gameState
                            .CraftingLookupDict?.GetValueOrNull(job.Code)
                            ?.FirstOrDefault(ingredient => ingredient.Code == item.Code)
                            is not null;

                    return isIngredientOf;
                })
                is not null
            )
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

        if (itemToTurnIn is not null)
        {
            int amountInInventory =
                Character.GetItemFromInventory(itemToTurnIn.Value.Code)?.Quantity ?? 0;

            if (amountInInventory > 0)
            {
                await Character.NavigateTo("items", ContentType.TasksMaster);

                await Character.TaskTrade(
                    itemToTurnIn.Value.Code,
                    Math.Min(
                        Character.Schema.TaskTotal - Character.Schema.TaskProgress,
                        amountInInventory
                    )
                );

                if (amountInInventory >= itemToTurnIn.Value.Quantity)
                {
                    await Character.TaskComplete();
                }
            }
        }

        itemsToDeposit.Sort((a, b) => a.Importance.CompareTo(b.Importance));

        await Character.NavigateTo("bank", ContentType.Bank);

        foreach (var item in itemsToDeposit)
        {
            bool deposittingLowPrioItem = item.Importance <= ItemImportance.Low;

            // Keep going if we are depositting low prio items
            if (!deposittingLowPrioItem && !ShouldKeepDepositingIfAtBank(Character))
            {
                break;
            }

            int amountToDeposit = 0;

            if (deposittingLowPrioItem)
            {
                amountToDeposit = item.Quantity;
            }
            else
            {
                amountToDeposit = Math.Min(
                    item.Quantity,
                    MAX_FREE_INVENTORY_SPACES - Character.GetInventorySpaceLeft()
                );

                if (amountToDeposit <= 0)
                {
                    logger.LogWarning(
                        $"{JobName}: [{Character.Schema.Name}] Amount to deposit is {amountToDeposit} - shouldn't happen"
                    );
                }
            }

            await Character.DepositBankItem(
                [new WithdrawOrDepositItemRequest { Code = item.Code, Quantity = amountToDeposit }]
            );
        }

        // if (Character.GetInventorySpaceLeft() >= MIN_FREE_INVENTORY_SPACES)
        // {
        //     // TODO: Handle that we cannot tidy up enough - maybe spawn a HouseKeeping job? It would cook and craft items in the bank,
        //     // which often ends up taking up less space
        // }

        logger.LogInformation($"{JobName}: [{Character.Schema.Name}] completed");

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
