using Application.Jobs;

namespace Application;

public class GameLoader
{
    GameState _gameState;

    public GameLoader(GameState gameState)
    {
        _gameState = gameState;
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
            foreach (var character in _gameState._characters)
            {
                if (
                    character._character.CooldownExpiration != DateTime.MinValue
                    && (character._character.CooldownExpiration - DateTime.UtcNow).TotalSeconds > 0
                )
                {
                    continue;
                }
                if (character.idle)
                {
                    if (character._character.Name == "Leonidas")
                    {
                        character.QueueJob(new GatherJob(character, "gudgeon", 10, _gameState));
                    }
                    else
                    {
                        FightJob fightJob = new FightJob(character, "chicken", 10, _gameState);
                        character.QueueJob(fightJob);
                    }
                }

                _ = character.RunJob();
            }

            await Task.Delay(1 * 1000);
        }
    }
}
