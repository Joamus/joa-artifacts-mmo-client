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
                var cooldownExpiresIn = character._character.CooldownExpiration - now;
                if (cooldownExpiresIn.TotalSeconds > 0)
                {
                    continue;
                }
                if (character.idle && character._jobs.Count == 0)
                {
                    // if (character._character.Name != "LumberMilly")
                    // {
                    // if (character._character.Name != "Ramsey")
                    // // if (character._character.Name != "Beatrix")
                    // // if (character._character.Name != "Leonidas")
                    // {
                    //     continue;
                    // }
                    switch (character._character.Name)
                    {
                        case "Leonidas":
                            character.QueueJob(new ObtainItem(character, "copper_dagger", 1));
                            break;
                        case "Ramsey":
                            character.QueueJob(new ObtainItem(character, "wooden_shield", 1));
                            break;
                        case "NatPagle":
                            FightMonster fightJob = new FightMonster(character, "green_slime", 10);
                            character.QueueJob(fightJob);
                            break;
                        case "Beatrix":
                            // character.QueueJob(new DepositUnneededItems(character));
                            // character.QueueJob(new ObtainJob(character, "small_health_potion", 20));
                            // character.QueueJob(new ObtainItem(character, "copper", 5));
                            character.QueueJob(new ObtainItem(character, "copper_ring", 1));
                            // character.QueueJob(new GatherJob(character, "sunflower", 90));
                            break;
                        case "LumberMilly":
                            // character.QueueJob(new CookEverythingInInventory(character));
                            // character.QueueJob(new DepositUnneededItems(character));
                            // character.QueueJob(new ObtainItem(character, "cooked_gudgeon", 10));
                            // character.QueueJob(new ObtainItem(character, "cooked_gudgeon", 10));
                            character.QueueJob(new ObtainItem(character, "copper", 5));
                            break;

                        default:
                            character.QueueJob(new FightMonster(character, "yellow_slime", 10));
                            break;
                    }
                }

                _ = character.RunJob();
            }

            await Task.Delay(1 * 1000);
        }
    }
}
