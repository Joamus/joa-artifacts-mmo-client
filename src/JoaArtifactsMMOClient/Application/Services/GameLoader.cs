using Application.Jobs;
using Application.Services;
using Applicaton.Jobs;

namespace Application;

public class GameLoader
{
    readonly GameState _gameState;

    public GameLoader()
    {
        _gameState = GameServiceProvider.GetInstance().GetService<GameState>()!;
    }

    public static string LoadApiToken()
    {
        return Environment.GetEnvironmentVariable("TOKEN")
            ?? throw new Exception("No env variable \"TOKEN\" found");
    }

    public static string LoadAccountName()
    {
        return Environment.GetEnvironmentVariable("ACCOUNT")
            ?? throw new Exception("No env variable \"ACCOUNT\" found");
    }

    public async Task Start()
    {
        await GameLoop();
    }

    public async Task GameLoop()
    {
        bool running = true;

        while (running)
        {
            foreach (var character in _gameState.Characters)
            {
                var now = DateTime.UtcNow.AddSeconds(-2);
                var cooldownExpiresIn = character.Schema.CooldownExpiration - now;
                if (cooldownExpiresIn.TotalSeconds > 0)
                {
                    continue;
                }
                // if (character.Jobs.Count > 0)
                if (character.Idle)
                {
                    var _ = character.RunJob();
                }
            }

            if (_gameState.ShouldReload())
            {
                await _gameState.ReloadAll();
            }

            await Task.Delay(1 * 1000);
        }
    }
}
