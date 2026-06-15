using Application.Character;
using Application.Errors;
using Application.Services;
using Applicaton.Jobs.Chores;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public class RestockTasksCoins : CharacterJob, ICharacterChoreJob
{
    RestockTasksCoinsParams JobParams { get; init; }

    public RestockTasksCoins(
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

        int amountOfTasksCoins = await GetAmountOfTaskCoins();

        if (!ShouldRestock(amountOfTasksCoins))
        {
            return new None();
        }

        List<CharacterJob> jobs = [];

        AppError? error = null;

        for (int i = 0; i < JobParams.AmountOfJobsToDo; i++)
        {
            var result = await GetJobToGetCoins(Character, gameState);

            result.Switch(
                appError =>
                {
                    error = appError;
                },
                jobs.Add
            );

            if (error is not null)
            {
                return error;
            }
        }

        if (jobs.Count > 0)
        {
            await Character.QueueJobsAfter(Id, jobs);
        }

        logger.LogInformation(
            $"{JobName}: [{Character.Schema.Name}] run ended - queued {jobs.Count} x jobs"
        );

        return new None();
    }

    public static async Task<OneOf<AppError, CharacterJob>> GetJobToGetCoins(
        PlayerCharacter character,
        GameState gameState
    )
    {
        while (
            !await character.PlayerActionService.CanItemFromItemTaskShouldBeObtained()
            && await CancelTaskJob.CanCancelTask(character, gameState)
        )
        {
            await CancelTaskJob.DoCancelTask(character, gameState);
        }

        if (await character.PlayerActionService.CanItemFromItemTaskShouldBeObtained())
        {
            var job = new ItemTask(character, gameState);
            job.ForBank();

            return job;
        }

        return new AppError($"Cannot cancel the task for {character.Name} - job failed");
    }

    public static async Task<bool> CanDoJob(PlayerCharacter character, GameState gameState)
    {
        return GetJobToGetCoins(character, gameState) != null;
    }

    public async Task<bool> NeedsToBeDone()
    {
        int amountOfTasksCoins = await GetAmountOfTaskCoins();

        return ShouldRestock(amountOfTasksCoins);
    }

    public async Task<int> GetAmountOfTaskCoins()
    {
        var bankResponse = await gameState.BankItemCache.GetBankItems(Character);

        return bankResponse.FirstOrDefault(item => item.Code == ItemService.TasksCoin)?.Quantity
            ?? 0;
    }

    public bool ShouldRestock(int amountOfTasksCoins)
    {
        return amountOfTasksCoins < JobParams.LowerCoinsThreshold;
    }

    static RestockTasksCoinsParams GetJobParams(ChorePriority priority)
    {
        return priority switch
        {
            ChorePriority.Low => new RestockTasksCoinsParams
            {
                AmountOfJobsToDo = 5,
                LowerCoinsThreshold = 400,
            },
            ChorePriority.High => new RestockTasksCoinsParams
            {
                AmountOfJobsToDo = 10,
                LowerCoinsThreshold = 100,
            },
            _ => throw new NotImplementedException(),
        };
    }
}

public record RestockTasksCoinsParams
{
    public required int LowerCoinsThreshold { get; init; }
    public required int AmountOfJobsToDo { get; init; }
}
