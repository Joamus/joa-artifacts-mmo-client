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
        bool firstRun = true;

        while (true)
        {
            if (_gameState.ShouldReload())
            {
                await _gameState.ReloadAll();
            }

            if (firstRun)
            {
                firstRun = false;

                foreach (var playerAI in _gameState.CharacterAIs)
                {
                    // await HandleCharacterLoop(playerAI);
                    _ = StartCharacterLoop(playerAI);
                }
            }

            await Task.Delay(5 * 1000);
        }
    }

    async Task StartCharacterLoop(PlayerAI playerAI)
    {
        while (true)
        {
            try
            {
                await HandleCharacterLoop(playerAI);
            }
            catch (Exception e)
            {
                Logger.LogError(
                    "HandleCharacterLoop: [{Name}]: Failed job in loop - threw exception: {e.Message} - stack {e.StackTrace} - source: {e.Source}",
                    playerAI.Character.Name,
                    e.Message,
                    e.StackTrace,
                    e.Source
                );
            }
        }
    }

    async Task HandleCharacterLoop(PlayerAI playerAI)
    {
        // var now = DateTime.UtcNow.AddSeconds(-20);
        // var cooldownExpiresIn = playerAI.Character.Schema.CooldownExpiration - now;

        // Logger.LogInformation(
        //     "GameLoop: [{Name}]: Running AI loop - idle: {Idle} - cooldown expires in {cooldownExpiration}",
        //     playerAI.Character.Name,
        //     playerAI.Character.Idle,
        //     cooldownExpiresIn.TotalSeconds
        // );

        // if (cooldownExpiresIn.TotalSeconds > 0)
        // {
        //     continue;
        // }

        if (playerAI.Character.Idle)
        {
            playerAI.Character.CleanupOldWishlistItems();

            if (playerAI.Enabled)
            {
                if (playerAI.Character.CurrentJob is null && playerAI.Character.Jobs.Count == 0)
                {
                    Logger.LogInformation(
                        "HandleCharacterLoop: [{Name}]: Running AI loop - getting next job and queueing it",
                        playerAI.Character.Name
                    );

                    var job = await playerAI.GetNextJob();

                    if (job is not null)
                    {
                        await playerAI.Character.QueueJob(job);
                    }
                }
            }

            Logger.LogDebug("HandleCharacterLoop: [{Name}]: Run job", playerAI.Character.Name);

            await playerAI.Character.RunJob();
        }

        await Task.Delay(1 * 1000);
    }
}
