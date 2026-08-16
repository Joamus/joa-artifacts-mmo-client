using Application.Errors;
using Application.Jobs;
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

                foreach (var character in _gameState.Characters)
                {
                    character.ClearJobStats();
                }
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
        //

        var character = playerAI.Character;

        try
        {
            if (character.Idle)
            {
                character.CleanupOldWishlistItems();

                // if (character.CurrentJob is null && character.FindNextJobFromQueue() == null)
                if (character.CurrentJob is null && character.FindNextJobFromQueue() == null)
                {
                    if (character.CurrentJobOrchestrator is not null)
                    {
                        var nextBossJobResult = await character.CurrentJobOrchestrator.GetNextJobs(
                            character
                        );

                        AppError? bossAppError = null;

                        List<CharacterJob> nextJobs = [];

                        nextBossJobResult.Switch(
                            error =>
                            {
                                bossAppError = error;
                            },
                            nextBossJob =>
                            {
                                // If it returns empty, it does so after a waiting period - nothing else to do, just wait for now
                                nextJobs = nextBossJob;
                            }
                        );

                        if (bossAppError is not null)
                        {
                            await character.LeaveBossFightJob();

                            // Nothing listens to return of this function, but we'll return anyway.
                            // throw bossAppError;
                        }

                        if (nextJobs is not null && nextJobs.Count > 0)
                        {
                            Logger.LogInformation(
                                "HandleCharacterLoop: [{Name}]: Found jobs for boss fight with {bossFightMonster} - found {amountOfJobs} jobs",
                                character.Name,
                                character.CurrentJobOrchestrator.Monster.Code,
                                nextJobs.Count
                            );

                            foreach (var job in nextJobs)
                            {
                                await character.QueueJob(job);
                            }
                        }
                    }
                    else if (playerAI.Enabled)
                    {
                        Logger.LogInformation(
                            "HandleCharacterLoop: [{Name}]: Running AI loop - getting next job and queueing it",
                            character.Name
                        );

                        var job = await playerAI.GetNextJob();

                        if (job is not null)
                        {
                            await character.QueueJob(job);
                        }
                    }

                    Logger.LogDebug(
                        "HandleCharacterLoop: [{Name}]: Run job",
                        playerAI.Character.Name
                    );
                }
                await character.RunJob();
            }
        }
        catch (Exception e)
        {
            Logger.LogError(
                "HandleCharacterLoop: [{Name}]: Failed getting a new job - error: {message} - stack: {stack}",
                playerAI.Character.Name,
                e.Message,
                e.StackTrace
            );

            await character.ResetAfterJobFail();
        }

        await Task.Delay(1 * 1000);
    }
}
