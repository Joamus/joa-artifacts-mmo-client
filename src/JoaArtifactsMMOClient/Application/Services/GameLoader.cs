using Application.Services;

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
            if (_gameState.ShouldReload())
            {
                await _gameState.ReloadAll();
            }

            foreach (var playerAI in _gameState.CharacterAIs)
            {
                playerAI.Character.CleanupOldWishlistItems();

                var now = DateTime.UtcNow.AddSeconds(-2);
                var cooldownExpiresIn = playerAI.Character.Schema.CooldownExpiration - now;
                if (cooldownExpiresIn.TotalSeconds > 0)
                {
                    continue;
                }

                // if (character.Jobs.Count > 0)
                if (playerAI.Character.Idle)
                {
                    if (playerAI.Enabled)
                    {
                        if (
                            playerAI.Character.CurrentJob is null
                            && playerAI.Character.Jobs.Count == 0
                        )
                        {
                            var job = await playerAI.GetNextJob();

                            // Change
                            await playerAI.Character.QueueJob(job);
                        }
                    }
                    var _ = playerAI.Character.RunJob();
                }
            }

            await Task.Delay(1 * 1000);
        }
    }
}
