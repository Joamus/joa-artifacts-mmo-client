using Application.Services;

namespace Application;

public class GameLoader
{
    readonly GameState _gameState;

    public GameLoader()
    {
        _gameState = GameServiceProvider.GetInstance().GetService<GameState>()!;
        Logger = AppLogger.loggerFactory.CreateLogger<GameLoader>();
    }

    public ILogger Logger { get; init; }

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
                GC.Collect();
            }

            foreach (var playerAI in _gameState.CharacterAIs)
            {
                playerAI.Character.CleanupOldWishlistItems();

                var now = DateTime.UtcNow.AddSeconds(-2);
                var cooldownExpiresIn = playerAI.Character.Schema.CooldownExpiration - now;

                // Logger.LogInformation(
                //     "GameLoop: [{Name}]: Running AI loop - idle: {Idle} - cooldown expires in {cooldownExpiration}",
                //     playerAI.Character.Name,
                //     playerAI.Character.Idle,
                //     cooldownExpiresIn.TotalSeconds
                // );

                if (cooldownExpiresIn.TotalSeconds > 0)
                {
                    continue;
                }

                if (playerAI.Character.Idle)
                {
                    if (playerAI.Enabled)
                    {
                        if (
                            playerAI.Character.CurrentJob is null
                            && playerAI.Character.Jobs.Count == 0
                        )
                        {
                            Logger.LogInformation(
                                "GameLoop: [{Name}]: Running AI loop - getting next job and queueing it",
                                playerAI.Character.Name
                            );

                            playerAI.Character.Busy = true;
                            var job = await playerAI.GetNextJob();

                            // Change
                            _ = playerAI.Character.QueueJob(job);
                        }
                    }
                    Logger.LogDebug("GameLoop: [{Name}]: Run job", playerAI.Character.Name);

                    _ = Task.Run(async () =>
                    {
                        playerAI.Character.Busy = true;
                        await playerAI.Character.RunJob();

                        playerAI.Character.Busy = false;
                    });
                }
            }

            await Task.Delay(1 * 1000);
        }
    }
}
