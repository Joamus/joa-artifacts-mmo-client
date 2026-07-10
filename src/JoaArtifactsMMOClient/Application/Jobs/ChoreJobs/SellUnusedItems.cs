using Application.ArtifactsApi.Schemas;
using Application.ArtifactsApi.Schemas.Requests;
using Application.Character;
using Application.Errors;
using Application.Services;
using Applicaton.Jobs.Chores;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public class SellUnusedItems : CharacterJob, ICharacterChoreJob
{
    public const int SELL_LEVEL_DIFF = 10;

    public const bool SELL_SMALL_PEARLS_IF_FULL_PERFECT_PEARLS = true;

    public SellUnusedItems(PlayerCharacter playerCharacter, GameState gameState)
        : base(playerCharacter, gameState) { }

    protected override async Task<OneOf<AppError, None>> ExecuteAsync()
    {
        List<DropSchema> items = await GetItemsToSell();

        logger.LogInformation(
            $"{JobName}: [{Character.Schema.Name}] running - found {items.Count} different items to deposit"
        );

        if (items.Count == 0)
        {
            return new None();
        }

        Dictionary<string, List<DropSchema>> npcToItemsDict = [];

        foreach (var item in items)
        {
            var matchingItem = gameState.NpcItemsDict.GetValueOrNull(item.Code)!;

            string npc = matchingItem.Npc;

            if (npcToItemsDict.GetValueOrDefault(npc) is null)
            {
                npcToItemsDict.Add(npc, []);
            }

            npcToItemsDict.GetValueOrDefault(npc)!.Add(item);
        }

        foreach (var npc in npcToItemsDict)
        {
            bool doneSellingAllItems = false;

            while (!doneSellingAllItems)
            {
                bool allAtZero = true;

                foreach (var item in npc.Value)
                {
                    var bankResponse = await gameState.BankItemCache.GetBankItems(Character, true);

                    int amountInBank =
                        bankResponse
                            .FirstOrDefault(bankItem => bankItem.Code == item.Code)
                            ?.Quantity ?? 0;

                    int amountToWithdraw = Math.Min(
                        Character.GetAvailableInventorySpace(),
                        amountInBank
                    );

                    int amountInInventory =
                        Character.GetItemFromInventory(item.Code)?.Quantity ?? 0;

                    allAtZero = item.Quantity == 0;

                    if (amountToWithdraw > 0)
                    {
                        await Character.NavigateTo("bank");

                        await Character.WithdrawBankItem(
                            new List<WithdrawOrDepositItemRequest>
                            {
                                new WithdrawOrDepositItemRequest
                                {
                                    Code = item.Code,
                                    Quantity = amountToWithdraw,
                                },
                            }
                        );

                        item.Quantity -= amountToWithdraw;
                    }

                    if (
                        amountToWithdraw == 0
                        && (Character.GetItemFromInventory(item.Code)?.Quantity ?? 0) == 0
                    )
                    {
                        allAtZero = true;
                    }

                    if (
                        Character.GetAvailableInventorySpace() == 0 && amountInInventory > 0
                        || amountInInventory > 0 && amountInBank == 0
                    )
                    {
                        await SellAllItemsToNpc(npc.Key);
                    }
                }

                if (allAtZero)
                {
                    doneSellingAllItems = true;
                }
                else
                {
                    await SellAllItemsToNpc(npc.Key);
                }
            }
        }

        return new None();
    }

    public async Task SellAllItemsToNpc(string npcCode)
    {
        logger.LogInformation(
            $"{JobName}: [{Character.Schema.Name}] selling all items to \"{npcCode}\""
        );
        await Character.NavigateTo(npcCode);

        foreach (var item in Character.Schema.Inventory)
        {
            if (string.IsNullOrWhiteSpace(item.Code))
            {
                continue;
            }

            var npcItem = gameState.NpcItemsDict.GetValueOrNull(item.Code)!;

            if (npcItem?.Npc != npcCode)
            {
                continue;
            }

            var matchingItem = gameState.ItemsDict.GetValueOrNull(item.Code)!;

            if (!IsSellableTrashItem(matchingItem, gameState, true))
            {
                continue;
            }

            await Character.NpcSellItem(item.Code, item.Quantity);
        }
    }

    public Dictionary<string, NpcSchema> GetActiveNpcs()
    {
        Dictionary<string, NpcSchema> npcs = [];

        foreach (var npc in gameState.Npcs)
        {
            var npcEvent = gameState.EventService.EventEntitiesDict.GetValueOrNull(npc.Code);

            if (npcEvent is not null)
            {
                if (gameState.EventService.WhereIsEntityActive(npcEvent.Code) is null)
                {
                    continue;
                }
            }

            npcs.Add(npc.Code, npc);
        }

        return npcs;
    }

    public async Task<List<DropSchema>> GetItemsToSell()
    {
        List<DropSchema> items = [];

        var activeNpcs = GetActiveNpcs();

        var bankItems = await gameState.BankItemCache.GetBankItems(Character);

        int lowestCharacterLevel = RecycleUnusedItems.GetLowestCharacterLevel(gameState, true);

        int amountOfCharacters = gameState.Characters.Count;

        Dictionary<string, List<ItemSchema>> toolsByEffect = [];

        var relevantEquipmentFromBank = RecycleUnusedItems.GetRelevantEquipment(
            gameState,
            [
                .. bankItems
                    .Select(item => gameState.ItemsDict[item.Code])
                    .Where(item => item.Craft is not null),
            ]
        );

        foreach (var item in bankItems)
        {
            // For now, we just want to sell "trash" items like golden_shrimp, holey_boot etc., so only items that have no value apart
            // Incorporate evaluating whether a "fight item" is still relevant (look at RecycleUnusedItems), else we can sell them, e.g forest_ring.
            var matchingNpcItem = gameState.NpcItemsDict.GetValueOrNull(item.Code);

            if (
                matchingNpcItem is null
                || !EventService.IsNpcActive(gameState, matchingNpcItem.Npc)
            )
            {
                continue;
            }

            var matchingItem = gameState.ItemsDict.GetValueOrNull(item.Code)!;

            // Sell all the unneeded small pearls - they currently don't have any other use than buying perfect peals.
            if (
                item.Code == "small_pearls"
                && SELL_SMALL_PEARLS_IF_FULL_PERFECT_PEARLS
                && await gameState.GetAmountOfItemFromAll("perfect_pearl")
                    >= gameState.Characters.Count
            )
            {
                items.Add(new DropSchema { Code = item.Code, Quantity = item.Quantity });
                continue;
            }

            if (
                lowestCharacterLevel
                <= Math.Min(matchingItem.Level + SELL_LEVEL_DIFF, PlayerCharacter.MAX_LEVEL)
            )
            {
                continue;
            }

            bool isEquipmentThatShouldBeSold =
                matchingItem.Craft is not null
                && ItemService.EquipmentItemTypes.Contains(matchingItem.Type)
                && matchingItem.Level <= lowestCharacterLevel
                && !relevantEquipmentFromBank.Contains(item.Code);

            if (isEquipmentThatShouldBeSold || IsSellableTrashItem(matchingItem, gameState))
            {
                items.Add(new DropSchema { Code = item.Code, Quantity = item.Quantity });
            }
        }

        return items;
    }

    public static bool IsSellableTrashItem(
        ItemSchema item,
        GameState gameState,
        bool allowCurrency = false
    )
    {
        if (item.Craft is not null || item.Type != "resource")
        {
            return false;
        }

        bool canBeSold = gameState.NpcItemsDict.GetValueOrNull(item.Code)?.SellPrice > 0;

        if (!canBeSold)
        {
            return false;
        }

        if (gameState.CraftingLookupDict.GetValueOrNull(item.Code) is not null)
        {
            return false;
        }

        if (allowCurrency)
        {
            return true;
        }

        var isUsedAsCurrency =
            gameState
                .NpcItemsDict.FirstOrDefault(npcItem => npcItem.Value.Currency == item.Code)
                .Value
                is not null;

        return !isUsedAsCurrency;
    }

    public async Task<bool> NeedsToBeDone()
    {
        List<DropSchema> items = await GetItemsToSell();

        return items.Count > 0;
    }
}
