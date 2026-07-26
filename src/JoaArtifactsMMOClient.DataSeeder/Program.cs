// Fetches reference data (items, resources, monsters, maps, etc.) from the ArtifactsMMO API
// and dumps it to JSON fixtures used by JoaArtifactsMMOClientTests.
//
// Run before `dotnet test`:
//   dotnet run --project src/JoaArtifactsMMOClient.DataSeeder/JoaArtifactsMMOClient.DataSeeder.csproj

using System.Text.Json;
using Application;
using Application.Services.ApiServices;
using Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

ILogger logger = AppLogger.GetLogger();

// dotnet run sets the working directory to this project's folder, so config is read
// from the main project's appsettings files rather than duplicating secrets here.
string mainProjectDir = Path.GetFullPath(
    Path.Combine(Directory.GetCurrentDirectory(), "..", "JoaArtifactsMMOClient")
);

IConfiguration configuration = new ConfigurationBuilder()
    .SetBasePath(mainProjectDir)
    .AddJsonFile("appsettings.json", optional: true)
    .AddJsonFile("appsettings.Local.json", optional: true)
    .Build();

string accountName = configuration["AccountName"]!;
if (string.IsNullOrWhiteSpace(accountName))
{
    throw new Exception("Account name not found");
}

string token = configuration["ApiToken"]!;
if (string.IsNullOrWhiteSpace(token))
{
    throw new Exception("API Token not found");
}

bool beta = configuration.GetValue<bool>("Beta");

ApiRequester apiRequester = new(token, beta);
AccountRequester accountRequester = new(apiRequester, accountName, logger);

string outputDir = Path.GetFullPath(
    Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "TestData")
);
Directory.CreateDirectory(outputDir);

await FetchAndSave(
    "items.json",
    () => FetchAllPages(async page => (await accountRequester.GetItems(page)).Data)
);
await FetchAndSave(
    "resources.json",
    () => FetchAllPages(async page => (await accountRequester.GetResources(page)).Data)
);
await FetchAndSave(
    "npcs.json",
    () => FetchAllPages(async page => (await accountRequester.GetNpcs(page)).Data)
);
await FetchAndSave(
    "monsters.json",
    () => FetchAllPages(async page => (await accountRequester.GetMonsters(page)).Data)
);
await FetchAndSave(
    "maps.json",
    () => FetchAllPages(async page => (await accountRequester.GetMaps(page)).Data)
);
await FetchAndSave(
    "npc_items.json",
    () => FetchAllPages(async page => (await accountRequester.GetNpcItems(page)).Data)
);
await FetchAndSave("tasks.json", () => accountRequester.GetTasks());
await FetchAndSave("tasks_rewards.json", () => accountRequester.GetTasksRewards());
await FetchAndSave(
    "achievements.json",
    async () => (await accountRequester.GetAchievements()).Data
);
await FetchAndSave(
    "events.json",
    () => FetchAllPages(async page => (await accountRequester.GetEvents(page)).Data)
);
await FetchAndSave(
    "active_events.json",
    () => FetchAllPages(async page => (await accountRequester.GetActiveEvents(page)).Data)
);

logger.LogInformation("Done seeding test data.");

async Task<List<T>> FetchAllPages<T>(Func<int, Task<List<T>>> getPageData)
{
    List<T> all = [];
    int page = 1;

    while (true)
    {
        List<T> data = await getPageData(page);

        if (data.Count == 0)
        {
            break;
        }

        all.AddRange(data);
        page++;
    }

    return all;
}

async Task FetchAndSave<T>(string fileName, Func<Task<T>> fetch)
{
    logger.LogInformation($"Fetching {fileName}...");

    T data = await fetch();

    string json = JsonSerializer.Serialize(data, ApiRequester.getJsonOptions());
    string path = Path.Combine(outputDir, fileName);

    await File.WriteAllTextAsync(path, json);

    logger.LogInformation($"Wrote {path}");
}
