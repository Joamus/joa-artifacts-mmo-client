using System.Net.WebSockets;
using Application;
using Application.ArtifactsApi.Schemas;
using Application.ArtifactsApi.Schemas.Requests;
using Application.ArtifactsApi.Schemas.Responses;
using Application.Character;
using Application.Errors;
using Application.Jobs;
using Application.Services;
using OneOf;
using OneOf.Types;

namespace Applicaton.Jobs;

public class DepositUnneededItems : CharacterJob
{
    public DepositUnneededItems(
        PlayerCharacter playerCharacter,
        GameState gameState,
        MonsterSchema? monsterSchema = null
    )
        : base(playerCharacter, gameState)
    {
        MonsterSchema = monsterSchema;
    }

    public MonsterSchema? MonsterSchema { get; set; }

    private static float NEXT_BANK_EXPANION_COST_PERCENTAGE_OF_TOTAL = 0.80f;

    private static int MIN_FREE_BANK_SLOTS = 10;

    // Deposit until hitting this threshold
    private static int MIN_FREE_INVENTORY_SLOTS = 5;
    private static int MAX_FREE_INVENTORY_SLOTS = 8;
    private static int MIN_FREE_INVENTORY_SPACES = 5;
    private static int MAX_FREE_INVENTORY_SPACES = 30;

    protected override async Task<OneOf<AppError, None>> ExecuteAsync()
    {
        logger.LogInformation($"{JobName}: [{Character.Schema.Name}] run started");

        List<DepositItemRecord> itemsToDeposit = [];
        (string Code, int Quantity)? itemToTurnIn = null;

        // Deposit least important items

        var accountRequester = gameState.AccountRequester;

        var result = await gameState.BankItemCache.GetBankItems(Character);

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

        Dictionary<string, DepositItemRecord> bestToolDictionary = [];

        Dictionary<string, List<DepositItemRecord>> equipmentToKeep = [];

        await BuyBankSpaceIfNeeded();

        var bestFightItems = MonsterSchema is not null
            ? (
                await ItemService.GetBestFightItems(
                    Character,
                    gameState,
                    MonsterSchema,
                    Character
                        .Schema.Inventory.Where(item => !string.IsNullOrEmpty(item.Code))
                        .ToList()
                )
            ).ToDictionary(item => item.Code)
            : [];

        if (bestFightItems.Count > 0)
        {
            foreach (var item in bestFightItems)
            {
                await Character.PlayerActionService.SmartItemEquip(item.Key, item.Value.Quantity);
            }
        }

        foreach (var item in Character.Schema.Inventory)
        {
            if (string.IsNullOrEmpty(item.Code))
            {
                continue;
            }
            int amountInInventory = item.Quantity;

            ItemSchema matchingItem = gameState.ItemsDict.GetValueOrNull(item.Code)!;

            bool isEquipment = ItemService.EquipmentItemTypes.Contains(matchingItem.Type);

            int amountEquipped = isEquipment
                ? Character.GetEquippedItem(item.Code).Select(item => item.Quantity).Sum()
                : 0;

            bool isRing = matchingItem.Type == "ring";

            bool hasAllEquipped = isRing ? amountEquipped == 2 : amountEquipped == 1;

            // We might have fight equipments in our bag, that we haven't yet equipped, or need to use, but aren't at the moment (e.g. fishing but have a sword in our inventory)
            // in those cases, we want to deposit surplus ones. This mostly comes up if we find equipment from mobs, e.g. wolf ears, we want to deposit the ones that we don't use.
            if (
                isEquipment
                && (hasAllEquipped || amountInInventory > 1 || isRing && amountInInventory > 2)
            )
            {
                itemsToDeposit.Add(
                    new DepositItemRecord
                    {
                        Code = item.Code,
                        Quantity = item.Quantity,
                        Importance = ItemImportance.None,
                    }
                );

                continue;
            }

            if (item.Code == "")
            {
                continue;
            }
            bool itemIsUsedForTask = item.Code == Character.Schema.Task;

            if (itemIsUsedForTask)
            {
                // itemsToDeposit.Add((item.Code, item.Quantity, ItemImportance.High));
                itemToTurnIn = (item.Code, amountInInventory);
                continue;
            }

            if (ItemService.EquipmentItemTypes.Contains(matchingItem.Type))
            {
                var itemImportance = ItemImportance.High;

                if (matchingItem.Subtype == "tool")
                {
                    itemImportance = ItemImportance.VeryHigh;
                }

                itemsToDeposit.Add(
                    new DepositItemRecord
                    {
                        Code = item.Code,
                        Quantity = item.Quantity,
                        Importance = itemImportance,
                    }
                );
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
                        new DepositItemRecord
                        {
                            Code = item.Code,
                            Quantity = item.Quantity,
                            Importance = ItemImportance.Medium,
                        }
                    );
                }
                continue;
            }

            // Looking through all of the jobs is a bit iffy, because a job queued far ahead can need some item,
            // but we should probably just obtain it again if we don't have it. Changed this, because my character,
            // kept refusing to deposit sap, but didn't need it until later.

            var jobsWithItem = Character.Jobs.Where(job =>
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
            });

            foreach (var job in jobsWithItem)
            {
                int amountToDeposit =
                    job.Amount >= amountInInventory ? 0 : amountInInventory - job.Amount;
                if (amountToDeposit > 0)
                {
                    itemsToDeposit.Add(
                        new DepositItemRecord
                        {
                            Code = item.Code,
                            Quantity = amountToDeposit,
                            Importance = ItemImportance.High,
                        }
                    );
                    amountInInventory -= amountToDeposit;
                }
            }

            var quantityInBank = bankItems.ContainsKey(item.Code) ? bankItems[item.Code] : 0;
            // We can store like 9+ billion items in the bank, so no reason to check if we are gonna cap by storing more items.
            // We want to prioritize storing items in the bank that we already have in the bank.

            var importance = quantityInBank > 0 ? ItemImportance.None : ItemImportance.Low;

            itemsToDeposit.Add(
                new DepositItemRecord
                {
                    Code = item.Code,
                    Quantity = item.Quantity,
                    Importance = importance,
                }
            );
        }

        if (itemToTurnIn is not null && itemToTurnIn.Value.Quantity > 0)
        {
            // int amountInInventory =
            //     Character.GetItemFromInventory(itemToTurnIn.Value.Code)?.Quantity ?? 0;

            await Character.NavigateTo("items");

            int amountLeft = Character.Schema.TaskTotal - Character.Schema.TaskProgress;

            await Character.TaskTrade(
                itemToTurnIn.Value.Code,
                Math.Min(
                    Character.Schema.TaskTotal - Character.Schema.TaskProgress,
                    itemToTurnIn.Value.Quantity
                )
            );
        }

        foreach (var item in itemsToDeposit)
        {
            if (item.Importance > ItemImportance.Low)
            {
                continue;
            }
            var matchingItem = gameState.ItemsDict.GetValueOrNull(item.Code)!;
            // We want to skip non-equipment, but also equipment we have deemed low prio anyway
            if (!ItemService.EquipmentItemTypes.Contains(matchingItem.Type))
            {
                continue;
            }

            if (matchingItem.Subtype == "tool")
            {
                var toolEffect = matchingItem.Effects.Find(effect =>
                    SkillService.GetSkillFromName(effect.Code) is not null
                );

                if (toolEffect is null)
                {
                    // Could happen, but then we don't care I guess?
                    continue;
                }

                DepositItemRecord? currentBest = bestToolDictionary.GetValueOrNull(
                    toolEffect.Code!
                );

                var currentBestItem = currentBest is not null
                    ? gameState.ItemsDict.GetValueOrNull(currentBest.Code)
                    : null;

                // Remember, the lower the value, the better for gathering tool effects
                if (
                    currentBestItem is null
                    || !currentBestItem.Effects.Exists(effect =>
                        effect.Code == toolEffect.Code && effect.Value < toolEffect.Value
                    )
                )
                {
                    bestToolDictionary.Add(toolEffect.Code, item);
                }

                if (currentBest is not null)
                {
                    currentBest.Importance = ItemImportance.None;
                }
            }
        }

        foreach (var item in itemsToDeposit)
        {
            var matchingItem = gameState.ItemsDict.GetValueOrNull(item.Code)!;

            if (
                !ItemService.EquipmentItemTypes.Contains(matchingItem.Type)
                || matchingItem.Subtype == "tool"
            )
            {
                continue;
            }

            if (
                MonsterSchema is not null
                && ItemService.EquipmentItemTypes.Contains(matchingItem.Type)
                && !bestFightItems.ContainsKey(item.Code)
            )
            {
                item.Importance = ItemImportance.None;
            }

            if (item.Importance > ItemImportance.Low)
            {
                continue;
            }
        }

        itemsToDeposit.Sort((a, b) => a.Importance.CompareTo(b.Importance));

        await Character.NavigateTo("bank");

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
                int amountNeededToDeposit =
                    MAX_FREE_INVENTORY_SPACES - Character.GetInventorySpaceLeft();

                if (amountNeededToDeposit <= 0)
                {
                    amountNeededToDeposit = item.Quantity;
                }

                amountToDeposit = amountNeededToDeposit;

                // amountToDeposit = Math.Min(
                //     item.Quantity,
                //     Math.Max(MAX_FREE_INVENTORY_SPACES - Character.GetInventorySpaceLeft(), 0)
                // );

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

    public async Task BuyBankSpaceIfNeeded()
    {
        var result = await gameState.AccountRequester.GetBankDetails();

        if (
            result.Data.NextExpansionCost
            <= Character.Schema.Gold * NEXT_BANK_EXPANION_COST_PERCENTAGE_OF_TOTAL
        )
        {
            var itemsInBank = await gameState.AccountRequester.GetBankItems();

            int amountFree = result.Data.Slots - itemsInBank.Data.Count();

            if (amountFree <= MIN_FREE_BANK_SLOTS)
            {
                logger.LogInformation(
                    $"{JobName}: [{Character.Schema.Name}] buying bank expansions, free bank slots is {amountFree} - got ${Character.Schema.Gold} gold, next expansion costs ${result.Data.NextExpansionCost}"
                );
                // Buy bank expansion
                await Character.NavigateTo("bank");
                await Character.BuyBankExpansion(Character.Schema.Name);
            }
        }
    }

    public static bool ShouldInitDepositItems(PlayerCharacter character, bool preJob = true)
    {
        bool hasTooLittleInventorySpace =
            character.GetInventorySpaceLeft()
            < (preJob ? MAX_FREE_INVENTORY_SPACES - 1 : MIN_FREE_INVENTORY_SPACES);

        if (hasTooLittleInventorySpace)
        {
            return true;
        }

        int amountOfEmptyInventorySlots = character.Schema.Inventory.Count(
            (item) => string.IsNullOrEmpty(item.Code)
        );

        bool hasTooFewInventorySlots =
            amountOfEmptyInventorySlots
            <= (preJob ? MIN_FREE_INVENTORY_SLOTS : MAX_FREE_INVENTORY_SLOTS);

        if (hasTooFewInventorySlots)
        {
            return true;
        }

        return false;
    }

    public static bool ShouldKeepDepositingIfAtBank(PlayerCharacter character)
    {
        bool hasEnoughInventorySpace =
            character.GetInventorySpaceLeft() < MAX_FREE_INVENTORY_SPACES;

        bool hasEnoughInventorySlots =
            character.Schema.Inventory.Count((item) => string.IsNullOrEmpty(item.Code))
            < MAX_FREE_INVENTORY_SLOTS;

        return hasEnoughInventorySlots && hasEnoughInventorySlots;
    }
}

public enum ItemImportance
{
    None = 0,
    Low = 10,
    Medium = 20,
    High = 30,
    VeryHigh = 40,
}

public record DepositItemRecord
{
    public string Code { get; set; } = "";
    public int Quantity { get; set; }
    public ItemImportance Importance { get; set; }
}
