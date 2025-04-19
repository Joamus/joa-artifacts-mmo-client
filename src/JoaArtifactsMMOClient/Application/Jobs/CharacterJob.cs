using Application.Character;
using Application.Errors;
using Application.Services;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public abstract class CharacterJob
{
    public Guid Id { get; init; }
    public PlayerCharacter _playerCharacter { get; init; }

    public GameState _gameState { get; init; }

    protected ILogger<CharacterJob> _logger { get; init; }

    protected bool _shouldInterrupt { get; set; }

    public string? _code { get; init; }

    protected CharacterJob(PlayerCharacter playerCharacter)
    {
        Id = Guid.NewGuid();
        _playerCharacter = playerCharacter;
        _logger = LoggerFactory.Create(AppLogger.options).CreateLogger<CharacterJob>();
        _gameState = GameServiceProvider.GetInstance().GetService<GameState>()!;
    }

    public abstract Task<OneOf<JobError, None>> RunAsync();

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
