using System.Text.Json.Serialization;
using Application.Character;
using Application.Errors;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public abstract class CharacterJob
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid? ParentCollabJobId { get; set; } = null;
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

    public delegate Task JobHook();

    public JobHook? onSuccessEndHook = null;

    public JobHook? onAfterSuccessEndHook = null;

    public JobHook? onJobQueuedHook = null;

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

            if (onAfterSuccessEndHook is not null)
            {
                await onAfterSuccessEndHook.Invoke();
                onAfterSuccessEndHook = null;
            }
        }
        return new None();
    }

    public virtual void Interrupt()
    {
        ShouldInterrupt = true;
    }

    public bool IsJobChildOfCollabJobId(Guid collabJobId)
    {
        if (ParentCollabJobId == collabJobId)
        {
            return true;
        }
        // We go a few levels deep - make a better solution at some point

        bool isChildOfCollabJob = false;
        int currentLevelsDeep = 0;
        int maxLevelsDeep = 5;

        CharacterJob? currentCharacterJob = this;

        while (!isChildOfCollabJob && currentLevelsDeep <= maxLevelsDeep)
        {
            if (currentCharacterJob is null)
            {
                break;
            }

            var currentParent = currentCharacterJob.ParentJob;

            isChildOfCollabJob = currentParent?.ParentCollabJobId == collabJobId;

            currentCharacterJob = currentParent;

            currentLevelsDeep += 1;
        }

        return isChildOfCollabJob;
    }
}

public enum JobStatus
{
    New,
    Completed,
    Suspend,
    Failed,
}
