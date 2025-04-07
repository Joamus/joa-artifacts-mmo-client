using Application;
using Infrastructure;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder
    .Services.AddEndpointsApiExplorer()
    .AddSwaggerGen(opt =>
    {
        opt.SwaggerDoc(
            "v1",
            new OpenApiInfo
            {
                Title = "Joas Artifacts Client",
                Description = "Why play a game when you can automate it",
                Version = "v1",
            }
        );
    });
builder.Services.AddCors();
builder.Services.AddProblemDetails();

ServiceCollection collection = new ServiceCollection();

string token = await GameLoader.LoadApiToken();

ApiRequester apiService = new ApiRequester(token);

collection.AddSingleton<ApiRequester>(apiService);

var app = builder.Build();

app.UseExceptionHandler();

app.UseSwagger();
app.UseSwaggerUI(opt =>
{
    opt.SwaggerEndpoint("/swagger/v1/swagger.json", "Joas Artifacts MMO Client");
});

Task apiTask = Task.Run(async () =>
{
    await app.RunAsync();
});
Task gameTask = Task.Run(async () =>
{
    GameLoader loader = new GameLoader();

    var _ = await loader.Start();
});

await Task.WhenAny([apiTask, gameTask]);
