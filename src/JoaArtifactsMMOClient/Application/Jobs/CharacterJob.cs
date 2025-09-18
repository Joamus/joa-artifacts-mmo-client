using System.Runtime.CompilerServices;
using Application.Character;
using Application.Errors;
using Application.Services;
using Newtonsoft.Json;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public abstract class CharacterJob
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public CharacterJob? ParentJob { get; private set; }

    [JsonIgnore]
    public PlayerCharacter Character { get; set; }

    [JsonIgnore]
    public GameState gameState { get; set; }

    [JsonIgnore]
    protected ILogger<CharacterJob> logger { get; init; } =
        LoggerFactory.Create(AppLogger.options).CreateLogger<CharacterJob>();

    [JsonIgnore]
    protected bool ShouldInterrupt { get; set; }

    public string Code { get; init; } = "";

    public delegate Task OnSuccessEndHook();

    public OnSuccessEndHook onSuccessEndHook = () =>
    {
        return Task.Run(() => { });
    };

    protected CharacterJob(PlayerCharacter playerCharacter, GameState gameState)
    {
        Character = playerCharacter;
        this.gameState = gameState;
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
                return appError;
        }
        await onSuccessEndHook.Invoke();
        return new None();
    }

    public virtual void Interrrupt()
    {
        ShouldInterrupt = true;
    }

    public virtual Task<List<CharacterJob>> GetJobs()
    {
        List<CharacterJob> jobs = [this];

        return Task.FromResult(jobs);
    }
}

enum JobPriority
{
    /** Jobs that are "idle" just mean that they are jobs the characters take up when not doing anything better. This possibly includes leveling up low level skills, maybe gearing up etc. */
    Idle = 0,
    Low = 1,
    High = 2,
}

public enum JobStatus
{
    Suspend,
}
