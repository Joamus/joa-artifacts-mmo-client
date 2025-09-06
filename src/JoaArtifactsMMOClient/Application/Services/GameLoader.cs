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
                var cooldownExpiresIn = character.Character.CooldownExpiration - now;
                if (cooldownExpiresIn.TotalSeconds > 0)
                {
                    continue;
                }
                if (character.idle && character.Jobs.Count == 0)
                {
                    // Try only from API
                    // continue;
                    // if (character._character.Name != "LumberMilly")
                    // {
                    // if (character._character.Name != "Ramsey")
                    // if (character._character.Name != "Beatrix")
                    // {
                    //     // if (character._character.Name != "Leonidas")
                    //     // if (character._character.Name != "Leonidas")
                    //     // {
                    //     continue;
                    // }

                    // TODO: Next time, have them finish their monster quests - only take gather quests from now on
                    switch (character.Character.Name)
                    {
                        case "Leonidas":
                            // character.QueueJob(new ObtainItem(character, "copper_dagger", 1));
                            if (character.Character.TaskType == "monsters")
                            {
                                character.QueueJob(new MonsterTask(character));
                            }
                            else
                            {
                                // character.QueueJob(new ObtainItem(character, "water_bow", 1));
                                character.QueueJob(new ObtainItem(character, "red_slimeball", 10));
                            }
                            break;
                        case "Ramsey":
                            if (character.Character.TaskType == "monsters")
                            {
                                character.QueueJob(new MonsterTask(character));
                            }
                            else
                            {
                                character.QueueJob(new ObtainItem(character, "wooden_shield", 1));
                            }
                            break;
                        case "NatPagle":
                            // character.QueueJob(new FightMonster(character, "green_slime", 10));
                            // character.QueueJob(new FightMonster(character, "blue_slime", 10));
                            // character.QueueJob(new FightMonster(character, "blue_slime", 10));
                            // character.QueueJob(new ObtainItem(character, "ash_plank", 6));
                            // character.QueueJob(new ObtainItem(character, "blue_slimeball", 10));
                            // character.QueueJob(new ObtainItem(character, "red_slimeball", 10));
                            character.QueueJob(new ObtainItem(character, "copper", 6));
                            character.QueueJob(new DepositItems(character, "copper", 6));
                            // character.QueueJob(new MonsterTask(character));
                            break;
                        case "Beatrix":
                            // character.QueueJob(new DepositUnneededItems(character));
                            // character.QueueJob(new ObtainJob(character, "small_health_potion", 20));
                            // character.QueueJob(new ObtainItem(character, "copper", 5));
                            // character.QueueJob(new ObtainItem(character, "copper_ring", 1));
                            if (character.Character.TaskType == "monsters")
                            {
                                character.QueueJob(new MonsterTask(character));
                            }
                            else
                            {
                                // character.QueueJob(new ObtainItem(character, "copper_ring", 1));
                                character.QueueJob(new ObtainItem(character, "life_amulet", 1));
                            }
                            // character.QueueJob(new GatherJob(character, "sunflower", 90));
                            break;
                        case "LumberMilly":
                            if (character.Character.TaskType == "monsters")
                            {
                                character.QueueJob(new MonsterTask(character));
                            }
                            else
                            {
                                character.QueueJob(new ObtainItem(character, "copper", 5));
                            }
                            // character.QueueJob(new CookEverythingInInventory(character));
                            // character.QueueJob(new DepositUnneededItems(character));
                            // character.QueueJob(new ObtainItem(character, "cooked_gudgeon", 10));
                            // character.QueueJob(new ObtainItem(character, "cooked_gudgeon", 10));
                            break;

                        default:
                            character.QueueJob(new FightMonster(character, "yellow_slime", 10));
                            break;
                    }
                }

                var _ = character.RunJob();
            }

            await Task.Delay(1 * 1000);
        }
    }
}
