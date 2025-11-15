using Application.Character;
using Application.Errors;
using Application.Services;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public class CompleteTask : CharacterJob
{
    public static readonly int PRICE_OF_EXCHANGE = 6;
    public string? ItemCode { get; init; }
    public int? ItemAmount { get; init; }

    public CompleteTask(
        PlayerCharacter playerCharacter,
        GameState gameState,
        string? itemCode,
        int? itemAmount
    )
        : base(playerCharacter, gameState)
    {
        Code = Character.Schema.TaskType;
        ItemCode = itemCode;
        ItemAmount = itemAmount;
    }

    protected override async Task<OneOf<AppError, None>> ExecuteAsync()
    {
        if (
            Character.Schema.Task == ""
            || Character.Schema.TaskProgress < Character.Schema.TaskTotal
        )
        {
            // Cannot complete quest, ignore for now
            return new None();
        }

        logger.LogInformation($"{JobName}: [{Character.Schema.Name}] run started - task {Code}");

        var matchingItem = ItemCode is not null
            ? gameState.NpcItemsDict.GetValueOrNull(ItemCode)
            : null;

        await Character.NavigateTo(Code!);

        await Character.TaskComplete();

        var taskCoinsAmount =
            Character
                .Schema.Inventory.FirstOrDefault(item => item.Code == ItemService.TasksCoin)
                ?.Quantity ?? 0;

        if (matchingItem is not null)
        {
            if (ItemAmount is null)
            {
                return new AppError($"ItemAmount should not be null if ItemCode is not null");
            }

            int itemAmount = (int)ItemAmount;

            if (
                matchingItem.Currency == ItemService.TasksCoin
                && matchingItem.BuyPrice * ItemAmount >= taskCoinsAmount
            )
            {
                logger.LogInformation(
                    $"{JobName}: [{Character.Schema.Name}] buying item \"{ItemCode}\" for {matchingItem.BuyPrice} per item (total: {matchingItem.BuyPrice * ItemAmount}) from \"tasks_master\" - have {taskCoinsAmount} total tasks_coin - for {Character.Schema.Name} - task {Code}"
                );
                await Character.NavigateTo("tasks_trader");
                await Character.NpcBuyItem(matchingItem.Code, itemAmount);
            }
        }
        // The item cannot be bought directly, only obtainable as a random reward
        else if (ItemCode is not null)
        {
            var taskCoins = Character.Schema.Inventory.FirstOrDefault(item =>
                item.Code == ItemService.TasksCoin
            );

            if (taskCoinsAmount >= PRICE_OF_EXCHANGE)
            {
                int amountOfItemNow = Character.GetItemFromInventory(ItemCode)?.Quantity ?? 0;

                logger.LogInformation(
                    $"{JobName}: [{Character.Schema.Name}] exchanged {PRICE_OF_EXCHANGE} \"tasks_coin\" for a random reward, to get {ItemCode} - task {Code}"
                );

                var result = await Character.TaskExchange();

                logger.LogInformation(
                    $"{JobName}: [{Character.Schema.Name}] rewards were gold: {result.Data.Rewards.Gold} and items: {result.Data.Rewards.Items} - task {Code}"
                );

                var itemAsReward = result.Data.Rewards.Items.Find(item => item.Code == ItemCode);

                if (itemAsReward?.Quantity >= ItemAmount)
                {
                    logger.LogInformation(
                        $"{JobName}: [{Character.Schema.Name}] found {itemAsReward?.Quantity} of the items we needed, out of {ItemAmount} - task {Code}"
                    );
                }

                logger.LogInformation(
                    $"{JobName}: [{Character.Schema.Name}] exchanged {PRICE_OF_EXCHANGE} \"tasks_coin\" for a random reward, to get {ItemCode} - task {Code}"
                );
            }
        }
        else
        {
            logger.LogInformation(
                $"{JobName}: [{Character.Schema.Name}] just completed task for task coins - got {taskCoinsAmount} x \"task_coins\" - task {Code}"
            );
        }

        logger.LogInformation($"{JobName}: [{Character.Schema.Name}] run complete - task {Code}");

        return new None();
    }
}
