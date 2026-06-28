using System.Collections.Immutable;
using Application.ArtifactsApi.Schemas;
using Application.Character;
using Application.Dtos;
using Application.Errors;
using Application.Services;
using Applicaton.Jobs.Chores;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public class RestockEventRelatedItems : CharacterJob, ICharacterChoreJob
{
    const int BASELINE_RESTOCK_TELEPORT_POTIONS_AMOUNT = 50;

    public RestockEventRelatedItems(PlayerCharacter playerCharacter, GameState gameState)
        : base(playerCharacter, gameState) { }

    protected override async Task<OneOf<AppError, None>> ExecuteAsync()
    {
        var nextJob = await GetNextJob();

        if (nextJob is not null)
        {
            await Character.QueueJobsAfter(Id, [nextJob]);
        }

        return new None();
    }

    public async Task<CharacterJob?> GetNextJob()
    {
        var levelRange = GameState.GetCharacterLevelRange(gameState);

        var bankItems = await gameState.BankItemCache.GetBankItems(Character);

        var itemToRestock = await GetNextTeleportPotionToRestock(gameState, bankItems, levelRange);

        if (itemToRestock is null)
        {
            return null;
        }

        return new ObtainItem(Character, gameState, itemToRestock.Code, itemToRestock.Quantity);
    }

    public async Task<DropSchema?> GetNextTeleportPotionToRestock(
        GameState gameState,
        List<DropSchema> bankItems,
        LevelRange levelRange
    )
    {
        var bankItemsDict = bankItems.ToDictionary(item => item.Code);

        int totalBudget = await gameState.BankItemCache.GetTotalBudgetInBank();

        if (totalBudget == 0)
        {
            return null;
        }

        var highestLevelCharacter = gameState.Characters.First(character =>
            character.Schema.Level == levelRange.Highest
        );

        List<(ItemSchema item, DropSchema drop)> result =
        [
            .. gameState
                .Items.Where(item =>
                    ItemService.IsTeleportPotion(item)
                    && ItemService.CanUseItem(item, highestLevelCharacter.Schema)
                )
                .Select(item =>
                {
                    int amountInBank = bankItemsDict.GetValueOrDefault(item.Code)?.Quantity ?? 0;

                    int totalAmountWanted = GetAmountOfTeleportPotionsToRestock(levelRange.Highest);

                    int quantity = 0;

                    if (amountInBank < totalAmountWanted)
                    {
                        totalAmountWanted -= amountInBank;

                        var matchingNpcItem = gameState.NpcItemsDict.GetValueOrNull(item.Code);

                        // These potions are obtained by buying them currently - could change
                        if (matchingNpcItem?.BuyPrice is not null)
                        {
                            int pricePerPotion = (int)matchingNpcItem.BuyPrice;

                            int amountWeCanBuy = totalBudget / pricePerPotion;

                            quantity = Math.Min(totalAmountWanted, amountWeCanBuy);

                            totalBudget -= pricePerPotion * quantity;
                        }
                    }

                    return (item, new DropSchema { Code = item.Code, Quantity = 0 });
                })
                .Where(item => item.Item2.Quantity > 0),
        ];

        result.Sort(
            (a, b) =>
                gameState.ItemsDict[b.drop.Code].Level - gameState.ItemsDict[a.drop.Code].Level
        );

        foreach ((ItemSchema item, DropSchema drop) in result)
        {
            if (await Character.PlayerActionService.CanObtainItem(item))
            {
                return drop;
            }
        }

        return null;
    }

    public async Task<bool> NeedsToBeDone()
    {
        return (await GetNextJob()) is not null;
    }

    public static int GetAmountOfTeleportPotionsToRestock(int maxCharacterLevel)
    {
        return Math.Max(
            BASELINE_RESTOCK_TELEPORT_POTIONS_AMOUNT,
            BASELINE_RESTOCK_TELEPORT_POTIONS_AMOUNT * maxCharacterLevel / 10
        );
    }
}
