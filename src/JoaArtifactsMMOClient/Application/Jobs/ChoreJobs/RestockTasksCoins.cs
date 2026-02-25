using Application.Character;
using Application.Errors;
using Application.Services;
using Applicaton.Jobs.Chores;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public class RestockTasksCoins : CharacterJob, ICharacterChoreJob
{
    const int AMOUNT_OF_JOBS_TO_DO = 20;
    const int LOWER_AMOUNT_THRESHOLD = 100;

    public RestockTasksCoins(PlayerCharacter playerCharacter, GameState gameState)
        : base(playerCharacter, gameState) { }

    protected override async Task<OneOf<AppError, None>> ExecuteAsync()
    {
        logger.LogInformation($"{JobName}: [{Character.Schema.Name}] run started");

        int amountOfTasksCoins = await GetAmountOfTaskCoins();

        if (HasEnoughTasksCoins(amountOfTasksCoins))
        {
            return new None();
        }

        List<CharacterJob> jobs = [];

        AppError? error = null;

        for (int i = 0; i < AMOUNT_OF_JOBS_TO_DO; i++)
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
            !await character.PlayerActionService.CanItemFromItemTaskBeObtained()
            && await CancelTask.CanCancelTask(character, gameState)
        )
        {
            await CancelTask.DoCancelTask(character, gameState);
        }

        if (await character.PlayerActionService.CanItemFromItemTaskBeObtained())
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

        return !HasEnoughTasksCoins(amountOfTasksCoins);
    }

    public async Task<int> GetAmountOfTaskCoins()
    {
        var bankResponse = await gameState.BankItemCache.GetBankItems(Character);

        return bankResponse
                .Data.FirstOrDefault(item => item.Code == ItemService.TasksCoin)
                ?.Quantity ?? 0;
    }

    public bool HasEnoughTasksCoins(int amountOfTasksCoins)
    {
        return amountOfTasksCoins >= LOWER_AMOUNT_THRESHOLD;
    }
}
