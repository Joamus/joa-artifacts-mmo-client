using Application.Character;
using Application.Errors;
using Application.Services;
using Newtonsoft.Json;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public abstract class CharacterJob
{
    public Guid Id { get; init; }

    [JsonIgnore]
    public PlayerCharacter _playerCharacter { get; init; }

    [JsonIgnore]
    protected GameState _gameState { get; init; }

    [JsonIgnore]
    protected ILogger<CharacterJob> _logger { get; init; }

    protected bool _shouldInterrupt { get; set; }

    public string? Code { get; init; }

    protected CharacterJob(PlayerCharacter playerCharacter, GameState gameState)
    {
        Id = Guid.NewGuid();
        _playerCharacter = playerCharacter;
        _logger = LoggerFactory.Create(AppLogger.options).CreateLogger<CharacterJob>();
        _gameState = gameState;
        // _gameState = GameServiceProvider.GetInstance().GetService<GameState>()!;
    }

    public abstract Task<OneOf<AppError, None>> RunAsync();

    public virtual void Interrrupt()
    {
        _shouldInterrupt = true;
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
