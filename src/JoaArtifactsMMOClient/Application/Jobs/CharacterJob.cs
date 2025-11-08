using System.Text.Json.Serialization;
using Application.Character;
using Application.Errors;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public abstract class CharacterJob
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string JobName { get; private set; } = "";
    public JobStatus Status = JobStatus.New;

    public CharacterJob? ParentJob { get; private set; }

    [JsonIgnore]
    public PlayerCharacter Character { get; set; }

    [JsonIgnore]
    public GameState gameState { get; set; }

    [JsonIgnore]
    public ILogger<CharacterJob> logger { get; init; } =
        AppLogger.loggerFactory.CreateLogger<CharacterJob>();

    [JsonIgnore]
    protected bool ShouldInterrupt { get; set; }

    public string Code { get; init; } = "";

    public int Amount { get; set; }

    public delegate Task OnSuccessEndHook();

    public OnSuccessEndHook? onSuccessEndHook = null;

    public virtual CharacterJob Clone()
    {
        return (CharacterJob)MemberwiseClone();
    }

    public T SetParent<T>(CharacterJob parentJob)
        where T : CharacterJob
    {
        ParentJob = parentJob;

        return (T)this;
    }

    protected CharacterJob(PlayerCharacter playerCharacter, GameState gameState)
    {
        Character = playerCharacter;
        this.gameState = gameState;

        JobName = GetType().Name + $" ({Id})";
    }

    protected abstract Task<OneOf<AppError, None>> ExecuteAsync();

    /**
    * This function is how the job is started. It's responsible for calling ExecuteAsync, and other hooks
    */
    public async Task<OneOf<AppError, None>> StartJobAsync()
    {
        var result = await ExecuteAsync();

        switch (result.Value)
        {
            case AppError appError:
                Status = JobStatus.Failed;
                onSuccessEndHook = null;
                return appError;
        }

        /**
         * No need to explictly set it in each ExecuteAsync job, we assume a job is completed unless it
         * was suspended or failed
         */
        if (Status == JobStatus.New)
        {
            Status = JobStatus.Completed;
        }

        if (Status == JobStatus.Completed)
        {
            if (onSuccessEndHook is not null)
            {
                await onSuccessEndHook.Invoke();
                onSuccessEndHook = null;
            }
        }
        return new None();
    }

    public virtual void Interrrupt()
    {
        ShouldInterrupt = true;
    }
}

public enum JobStatus
{
    New,
    Completed,
    Suspend,
    Failed,
}
