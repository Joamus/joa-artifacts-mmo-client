using System.Runtime.Serialization;
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
    public SellUnusedItems(PlayerCharacter playerCharacter, GameState gameState)
        : base(playerCharacter, gameState) { }

    protected override async Task<OneOf<AppError, None>> ExecuteAsync()
    {
        List<DropSchema> items = await GetItemsToSell();

        logger.LogInformation(
            $"{JobName}: [{Character.Schema.Name}] running - found {items.Count} different items to deposit"
        );

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
            await Character.NavigateTo(npc.Key);

            bool doneSellingAllItems = false;

            while (!doneSellingAllItems)
            {
                bool allAtZero = true;

                foreach (var item in npc.Value)
                {
                    if (item.Quantity > 0)
                    {
                        allAtZero = false;
                    }

                    int amountToWithdraw = Math.Min(
                        Character.GetInventorySpaceLeft(),
                        item.Quantity
                    );

                    if (amountToWithdraw == 0)
                    {
                        continue;
                    }

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

                    if (Character.GetInventorySpaceLeft() == 0)
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

            if (!IsSellableTrashItem(matchingItem, gameState))
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

        int lowestCharacterLevel = RecycleUnusedItems.GetLowestCharacterLevel(gameState);

        int amountOfCharacters = gameState.Characters.Count;

        Dictionary<string, List<ItemSchema>> toolsByEffect = [];

        var relevantEquipmentFromBank = RecycleUnusedItems.GetRelevantEquipment(
            gameState,
            bankItems
                .Data.Select(item => gameState.ItemsDict[item.Code])
                .Where(item => item.Craft is not null)
                .ToList()
        );

        foreach (var item in bankItems.Data)
        {
            // For now, we just want to sell "trash" items like golden_shrimp, holey_boot etc., so only items that have no value apart
            // Incorporate evaluating whether a "fight item" is still relevant (look at RecycleUnusedItems), else we can sell them, e.g forest_ring.
            var matchingNpcItem = gameState.NpcItemsDict.GetValueOrNull(item.Code);

            if (matchingNpcItem is null || !activeNpcs.ContainsKey(matchingNpcItem.Npc))
            {
                continue;
            }

            var matchingItem = gameState.ItemsDict.GetValueOrNull(item.Code)!;

            bool isEquipmentThatShouldBeSold =
                matchingItem.Craft is not null
                && ItemService.EquipmentItemTypes.Contains(matchingItem.Type)
                && matchingItem.Level <= lowestCharacterLevel
                && !relevantEquipmentFromBank.Contains(item.Code);

            if (!IsSellableTrashItem(matchingItem, gameState) || !isEquipmentThatShouldBeSold)
            {
                continue;
            }

            items.Add(new DropSchema { Code = item.Code, Quantity = item.Quantity });
        }

        return items;
    }

    public static bool IsSellableTrashItem(ItemSchema item, GameState gameState)
    {
        bool canBeSold = gameState.NpcItemsDict.GetValueOrNull(item.Code)?.SellPrice > 0;

        if (!canBeSold)
        {
            return false;
        }

        return item.Craft is null;
    }

    public Task<bool> NeedsToBeDone()
    {
        return Task.FromResult(true);
    }
}
