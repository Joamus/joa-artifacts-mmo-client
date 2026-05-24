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

                AppLogger
                    .GetLogger()
                    .LogDebug(
                        "GameLoop: [{Name}]: Running AI loop - idle: {Idle}",
                        playerAI.Character.Name,
                        playerAI.Character.Idle
                    );

                if (playerAI.Character.Idle)
                {
                    if (playerAI.Enabled)
                    {
                        if (
                            playerAI.Character.CurrentJob is null
                            && playerAI.Character.Jobs.Count == 0
                        )
                        {
                            AppLogger
                                .GetLogger()
                                .LogDebug(
                                    "GameLoop: [{Name}]: Running AI loop - getting next job and queueing it",
                                    playerAI.Character.Name
                                );

                            var job = await playerAI.GetNextJob();

                            // Change
                            _ = playerAI.Character.QueueJob(job);
                        }
                    }
                    AppLogger
                        .GetLogger()
                        .LogDebug("GameLoop: [{Name}]: Run job", playerAI.Character.Name);

                    _ = playerAI.Character.RunJob();
                }
            }

            await Task.Delay(1 * 1000);
        }
    }
}
