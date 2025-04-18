﻿using System.Text.Json;
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

await SetupGameServiceProvider(token, accountName);

var app = builder.Build();

// app.UseExceptionHandler();

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

async Task SetupGameServiceProvider(string token, string accountName)
{
    ServiceCollection collection = new ServiceCollection();

    ApiRequester apiRequester = new ApiRequester(token);
    AccountRequester accountRequester = new AccountRequester(apiRequester, accountName);

    _ = collection.AddSingleton(apiRequester);
    _ = collection.AddSingleton(accountRequester);

    GameState gameState = new GameState(accountRequester, apiRequester);

    collection.AddSingleton(gameState);

    var serviceProvider = collection.BuildServiceProvider();

    GameServiceProvider.SetInstance(serviceProvider);

    // var _gameState = GameServiceProvider.GetInstance().GetService<GameState>();
    await gameState.LoadAll();
}
