using System.ComponentModel;
using Application.ArtifactsApi.Schemas;
using Application.ArtifactsApi.Schemas.Requests;
using Application.Character;
using Application.Dtos;
using Application.Errors;
using Application.Jobs.Chores;
using Application.Services;
using Applicaton.Jobs;
using Applicaton.Jobs.Chores;
using Microsoft.OpenApi.Extensions;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public class GambleTasksCoins : CharacterJob, ICharacterChoreJob
{
    const int BASE_AMOUNT_TO_RESTOCK = 10;
    const float LEVEL_MULTIPLIER = 0.1f;
    GambleTasksCoinsParams JobParams { get; init; }

    public GambleTasksCoins(
        PlayerCharacter playerCharacter,
        GameState gameState,
        ChorePriority priority
    )
        : base(playerCharacter, gameState)
    {
        JobParams = GetJobParams(priority);
    }

    protected override async Task<OneOf<AppError, None>> ExecuteAsync()
    {
        logger.LogInformation($"{JobName}: [{Character.Schema.Name}] run started");

        var jobs = await GetJobs();

        if (jobs is not null)
        {
            await Character.QueueJobsAfter(Id, jobs);
        }

        return new None();
    }

    public async Task<List<CharacterJob>> GetJobs()
    {
        logger.LogInformation($"{JobName}: [{Character.Schema.Name}] run started");

        int amountOfTasksCoinsToWithdraw = await GetAmountOfTaskCoinsToWithdraw();

        if (amountOfTasksCoinsToWithdraw == 0)
        {
            return [];
        }

        int amountOfGambles = amountOfTasksCoinsToWithdraw / ItemService.GambleTasksCoinsPrice;

        if (amountOfGambles > 1)
        {
            var job = new WithdrawItem(
                Character,
                gameState,
                ItemService.TasksCoin,
                amountOfTasksCoinsToWithdraw,
                false
            )
            {
                onSuccessEndHook = async () =>
                {
                    List<string> taskMasters =
                    [
                        TaskType.monsters.GetDisplayName(),
                        TaskType.items.GetDisplayName(),
                    ];

                    var distances = taskMasters
                        .Select(
                            (master) =>
                            {
                                var steps = Character
                                    .PlayerActionService.NavigationService.GetAllStepsToDestination(
                                        master
                                    )
                                    .AsT1;

                                return (
                                    master,
                                    NavigationService.GetDistanceFromNavigationSteps(steps)
                                );
                            }
                        )
                        .ToList();

                    distances.Sort((a, b) => a.Item2 - b.Item2);

                    var closestTaskMaster = distances.First().master;

                    while (
                        (Character.GetItemFromInventory(ItemService.TasksCoin)?.Quantity ?? 0)
                        >= ItemService.GambleTasksCoinsPrice
                    )
                    {
                        await Character.NavigateTo(closestTaskMaster);
                        await Character.TaskExchange();
                    }
                },
            };

            return [job, new DepositUnneededItems(Character, gameState)];
        }

        return [];
    }

    public async Task<bool> NeedsToBeDone()
    {
        var jobs = await GetJobs();

        return jobs.Count > 0;
    }

    public async Task<List<DropSchema>> GetItemAmountsToRestock()
    {
        var highestCharacterLevel = gameState.GetCharacterLevelRange().Highest;

        var bankItems = await gameState.BankItemCache.GetBankItems(Character);

        var bankItemsDict = bankItems
            .Where(item => !string.IsNullOrWhiteSpace(item.Code))
            .ToDictionary(item => item.Code);

        var values = gameState
            .TaskItemsDict.Select(
                (item) =>
                {
                    var matchingItem = item.Value;

                    float dropRate = RestockResources.CalculateDropRate(
                        gameState
                            .TasksRewards.FirstOrDefault(reward => reward.Code == matchingItem.Code)
                            ?.Rate
                        ?? 0
                    );

                    int amountNeeded = (int)
                        Math.Floor(
                            highestCharacterLevel
                                * LEVEL_MULTIPLIER
                                * BASE_AMOUNT_TO_RESTOCK
                                / dropRate
                        );

                    return (matchingItem.Code, AmountNeeded: amountNeeded);
                }
            )
            .ToDictionary(element => element.Code);

        List<DropSchema> amountsToRestock =
        [
            .. values
                .Select(element =>
                {
                    var item = element.Value;

                    int amountInBank = bankItemsDict.GetValueOrNull(item.Code)?.Quantity ?? 0;

                    return new DropSchema
                    {
                        Code = item.Code,
                        Quantity =
                            amountInBank >= item.AmountNeeded
                                ? 0
                                : item.AmountNeeded - amountInBank,
                    };
                })
                .Where((item) => item.Quantity > 0),
        ];

        return amountsToRestock;
    }

    public async Task<int> GetAmountOfTaskCoinsToWithdraw()
    {
        var bankResponse = await gameState.BankItemCache.GetBankItems(Character);

        int amountInBank =
            bankResponse.FirstOrDefault(item => item.Code == ItemService.TasksCoin)?.Quantity ?? 0;

        int amountAboveThreshold =
            amountInBank > JobParams.MinimumCoinsThreshold
                ? amountInBank - JobParams.MinimumCoinsThreshold
                : 0;

        int amountOfGambles =
            Math.Min(Character.GetAvailableInventorySpace(), amountAboveThreshold)
            / ItemService.GambleTasksCoinsPrice;

        return amountOfGambles * ItemService.GambleTasksCoinsPrice;
    }

    static GambleTasksCoinsParams GetJobParams(ChorePriority priority)
    {
        return priority switch
        {
            _ => new GambleTasksCoinsParams
            {
                MinimumCoinsThreshold = 100,
                MinimumQuantityOfTaskItems = 10,
            },
        };
    }

    public bool IsAboveThreshold(int amountOfTasksCoins)
    {
        return amountOfTasksCoins > JobParams.MinimumCoinsThreshold;
    }
}

public record GambleTasksCoinsParams
{
    public required int MinimumCoinsThreshold { get; init; }
    public required int MinimumQuantityOfTaskItems { get; init; }
}
