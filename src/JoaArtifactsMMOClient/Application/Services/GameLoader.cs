using System.Globalization;
using System.Security.Principal;

namespace Application;

public class GameLoader
{
    public GameLoader() { }

    public static async Task<string> LoadApiToken()
    {
        using StreamReader reader = new("../../token.txt");

        return await reader.ReadToEndAsync();
    }

    public async Task<int> Start()
    {
        await LoadApiToken();

        await GameLoop();

        return 1;
    }

    public async Task GameLoop()
    {
        bool running = true;

        while (running)
        {
            //your code

            await Task.Delay(4500);
        }
    }
}
