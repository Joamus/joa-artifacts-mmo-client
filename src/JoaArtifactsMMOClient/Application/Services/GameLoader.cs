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

    public static async Task<string> LoadApiToken()
    {
        using StreamReader reader = new("../../token.txt");

        return await reader.ReadToEndAsync();
    }

    public static async Task<string> LoadAccountName()
    {
        using StreamReader reader = new("../../account.txt");

        return await reader.ReadToEndAsync();
    }

    public async Task<int> Start()
    {
        await GameLoop();

        return 1;
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

            await Task.Delay(1 * 1000);
        }
    }
}
