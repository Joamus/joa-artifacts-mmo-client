using System.Globalization;
using System.Security.Principal;

namespace Application;

public class GameLoader
{
    private string _token { get; set; } = "";

    public GameLoader() { }

    public async Task LoadApiToken()
    {
        using StreamReader reader = new("./token.txt");

        string text = await reader.ReadToEndAsync();

        _token = text;
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

            await System.Threading.Tasks.Task.Delay(4500);
        }
    }
}
