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
    public const int SELL_LEVEL_DIFF = RecycleUnusedItems.RECYCLE_LEVEL_DIFF;

    public const bool SELL_SMALL_PEARLS_IF_FULL_PERFECT_PEARLS = true;

    public static readonly List<string> SellableEquipmentItemTypes =
    [
        .. ItemService.EquipmentItemTypes.Where(itemType => itemType != "utility"),
    ];

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
            foreach (var item in npc.Value)
            {
                var bankResponse = await gameState.Services.BankItemCache.GetBankItems(
                    Character,
                    true
                );

                int amountInBank =
                    bankResponse.FirstOrDefault(bankItem => bankItem.Code == item.Code)?.Quantity
                    ?? 0;

                int amountToWithdraw = Math.Min(
                    Character.GetAvailableInventorySpace(),
                    amountInBank
                );

                int amountInInventory = Character.GetItemFromInventory(item.Code)?.Quantity ?? 0;

                if (amountToWithdraw > 0)
                {
                    await Character.NavigateTo(ContentCode.Bank);

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
                    amountInInventory += amountToWithdraw;
                }

                if (
                    Character.GetAvailableInventorySpace() == 0 && amountInInventory > 0
                    || amountInInventory > 0 && amountInBank == 0
                )
                {
                    await SellItemToNpc(npc.Key, item);
                }
            }
        }

        return new None();
    }

    public async Task SellItemToNpc(string npcCode, DropSchema item)
    {
        logger.LogInformation(
            $"{JobName}: [{Character.Schema.Name}] selling item {item.Quantity} x \"{item.Code}\""
        );

        await Character.NavigateTo(npcCode);

        await Character.NpcSellItem(item.Code, item.Quantity);
    }

    public Dictionary<string, NpcSchema> GetActiveNpcs()
    {
        Dictionary<string, NpcSchema> npcs = [];

        foreach (var npc in gameState.Npcs)
        {
            var npcEvent = gameState.Services.EventService.EventEntitiesDict.GetValueOrNull(
                npc.Code
            );

            if (
                npcEvent is not null
                && gameState.Services.EventService.WhereIsEntityActive(npcEvent.Code) is null
            )
            {
                continue;
            }

            npcs.Add(npc.Code, npc);
        }

        return npcs;
    }

    public async Task<List<DropSchema>> GetItemsToSell()
    {
        List<DropSchema> itemsToSell = [];

        var activeNpcs = GetActiveNpcs();

        var bankItems = await gameState.Services.BankItemCache.GetBankItems(Character);

        int lowestCharacterLevel = RecycleUnusedItems.GetLowestCharacterLevel(gameState, true);

        int amountOfCharacters = gameState.Characters.Count;

        Dictionary<string, List<ItemSchema>> toolsByEffect = [];

        var relevantEquipmentFromBank = RecycleUnusedItems.GetRelevantEquipment(
            gameState,
            [
                .. bankItems
                    .Select(item => gameState.ItemsDict[item.Code])
                    .Where(item => item.Craft is null),
            ]
        );

        foreach (var item in bankItems)
        {
            // For now, we just want to sell "trash" items like golden_shrimp, holey_boot etc., so only items that have no value apart
            // Incorporate evaluating whether a "fight item" is still relevant (look at RecycleUnusedItems), else we can sell them, e.g forest_ring.
            var matchingNpcItem = gameState.NpcItemsDict.GetValueOrNull(item.Code);

            if (
                matchingNpcItem is null
                || !activeNpcs.ContainsKey(matchingNpcItem.Npc)
                || matchingNpcItem.SellPrice is null
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
                itemsToSell.Add(new DropSchema { Code = item.Code, Quantity = item.Quantity });
                continue;
            }

            if (!IsItemLowEnoughLevelToSell(lowestCharacterLevel, matchingItem))
            {
                continue;
            }

            bool isEquipment = SellableEquipmentItemTypes.Contains(matchingItem.Type);

            bool isEquipmentThatShouldBeSold =
                matchingItem.Craft is not null
                && matchingItem.Type != "utility"
                && SellableEquipmentItemTypes.Contains(matchingItem.Type)
                && !relevantEquipmentFromBank.Contains(item.Code);

            // E.g. we have 50 wolf ears, but they are still an item we want to keep - we can sell everything off apart from 5
            if (isEquipment && !isEquipmentThatShouldBeSold)
            {
                int amountToKeep =
                    EquipmentService.GetAllowedItemAmount(matchingItem)
                    * gameState.Characters.Count;

                int amountToSell = item.Quantity - amountToKeep;

                if (amountToSell > 0)
                {
                    itemsToSell.Add(new DropSchema { Code = item.Code, Quantity = amountToSell });
                }
            }
            else if (isEquipmentThatShouldBeSold || IsSellableTrashItem(matchingItem, gameState))
            {
                itemsToSell.Add(new DropSchema { Code = item.Code, Quantity = item.Quantity });
            }
        }

        return itemsToSell;
    }

    public static bool IsItemLowEnoughLevelToSell(int lowestCharacterLevel, ItemSchema item)
    {
        return lowestCharacterLevel
            > Math.Min(item.Level + SELL_LEVEL_DIFF, PlayerCharacter.MAX_LEVEL);
        ;
    }

    public static bool IsSellableTrashItem(ItemSchema item, GameState gameState)
    {
        // It's used for the iron knife, but it's too valuable to keep hoarding
        if (item.Code == "shell")
        {
            return true;
        }
        if (item.Craft is not null)
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

        var isUsedAsCurrency =
            item.Type == "currency"
            || gameState
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
