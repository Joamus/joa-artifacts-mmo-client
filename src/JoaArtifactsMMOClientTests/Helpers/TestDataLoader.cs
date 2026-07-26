using System.Text.Json;
using Infrastructure;

namespace JoaArtifactsMMOClientTests.Helpers;

public static class TestDataLoader
{
    public static T Load<T>(string fileName)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "TestData", fileName);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Test data file '{fileName}' not found. Run: dotnet run --project src/JoaArtifactsMMOClient.DataSeeder before running tests.",
                path
            );
        }

        string json = File.ReadAllText(path);

        return JsonSerializer.Deserialize<T>(json, ApiRequester.getJsonOptions())!;
    }
}
