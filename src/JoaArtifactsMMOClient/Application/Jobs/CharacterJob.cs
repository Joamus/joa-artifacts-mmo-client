using Application.Character;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public abstract class CharacterJob
{
    protected PlayerCharacter _playerCharacter { get; init; }
    protected string _code { get; init; }
    protected int _amount { get; set; }

    protected int _progressAmount { get; set; } = 0;

    protected GameState _gameState { get; init; }

    protected ILogger<CharacterJob> _logger { get; init; }

    protected CharacterJob(
        PlayerCharacter playerCharacter,
        string code,
        int amount,
        GameState gameState
    )
    {
        _playerCharacter = playerCharacter;
        _code = code;
        _amount = amount;
        _gameState = gameState;
        _logger = LoggerFactory.Create(AppLogger.options).CreateLogger<CharacterJob>();
    }

    public abstract Task<OneOf<JobError, None>> RunAsync();
}
