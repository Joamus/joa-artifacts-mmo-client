using System.Text.Json;
using System.Text.Json.Serialization;
using Api.Endpoints;
using Application;
using Application.Services;
using Application.Services.ApiServices;
using Infrastructure;
using Microsoft.OpenApi.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

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

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(options =>
{
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

//convert Enums to Strings (instead of Integer) globally
JsonConvert.DefaultSettings = (
    () =>
    {
        var settings = new JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new SnakeCaseNamingStrategy(),
            },
        };

        settings.Converters.Add(
            new StringEnumConverter { NamingStrategy = new SnakeCaseNamingStrategy() }
        );
        return settings;
    }
);

string token = await GameLoader.LoadApiToken();
string accountName = await GameLoader.LoadAccountName();

GameState? gameState = SetupGameServiceProvider(builder.Services, token, accountName);

var app = builder.Build();

GameServiceProvider.SetInstance(app.Services);

app.UseExceptionHandler();

app.AddCharacterEndpoints();

app.UseSwagger();
app.UseSwaggerUI(opt =>
{
    opt.SwaggerEndpoint("/swagger/v1/swagger.json", "Joas Artifacts MMO Client");
});

GameLoader loader = new GameLoader();

await gameState.LoadAll();

_ = app.RunAsync();

await loader.Start();

// await Task.WhenAny([loader.Start(), app.RunAsync()]);


// var _ = await loader.Start();

// await app.RunAsync();

GameState SetupGameServiceProvider(IServiceCollection collection, string token, string accountName)
{
    ApiRequester apiRequester = new ApiRequester(token);
    AccountRequester accountRequester = new AccountRequester(apiRequester, accountName);

    collection.AddSingleton(apiRequester);
    collection.AddSingleton(accountRequester);

    gameState = new GameState(accountRequester, apiRequester);

    collection.AddSingleton(gameState);

    return gameState;
}
