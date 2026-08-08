using Application;
using Application.ArtifactsApi.Schemas;
using Application.ArtifactsApi.Schemas.Responses;
using Application.Character;
using Application.Services.ApiServices;
using Infrastructure;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace JoaArtifactsMMOClientTests.Helpers;

public static class ServiceHelper
{
    public static ApiRequester GetTestApiRequester()
    {
        return Substitute.For<ApiRequester>("dummy_token", false);
    }

    private static GameState? GameState;
    private static AccountRequester? AccountRequester;
    private static ApiRequester? ApiRequester;

    public static GameState GetEmptyGameState()
    {
        var apiRequester = GetTestApiRequester();

        GameState gameState = new(
            Substitute.For<AccountRequester>(
                apiRequester,
                "dummy_account_name",
                Substitute.For<ILogger>()
            ),
            apiRequester
        );

        return gameState;
    }

    // Loaded once from the seeded TestData fixtures (see JoaArtifactsMMOClient.DataSeeder) and
    // reused across calls - re-parsing these JSON files for every test would be wasteful, and the
    // data itself is treated as read-only reference data.
    private static readonly Lazy<ReferenceGameData> _referenceData = new(LoadReferenceData);

    // Builds a fresh GameState per call, populated via GameState's/EventService's real Load*
    // methods against a faked AccountRequester that serves the cached fixture data instead of
    // hitting the live API. Each call gets its own Characters/CharacterAIs, so tests can't leak
    // character state into each other, while the expensive JSON parsing only happens once.
    public static GameState GetPopulatedGameState(BankItemCache? bankItemCache = null)
    {
        // if (ApiRequester is not null && AccountRequester is not null)
        // if (GameState is not null)
        // {
        //     var newGameState = GameState;

        //     newGameState.Services = GameState.Services with
        //     {
        //         BankItemCache = GameState.Services.BankItemCache,
        //     };

        //     return newGameState;
        // }

        ReferenceGameData reference = _referenceData.Value;

        ApiRequester apiRequester = new("dummy_token", false);
        AccountRequester accountRequester = Substitute.For<AccountRequester>(
            apiRequester,
            "dummy_account_name",
            AppLogger.GetLogger()
        );

        accountRequester
            .GetMaps(Arg.Any<int>())
            .Returns(call => new MapsResponse
            {
                Data = FirstPageOnly(call.Arg<int>(), reference.Maps),
            });
        accountRequester
            .GetItems(Arg.Any<int>())
            .Returns(call => new ItemsResponse
            {
                Data = FirstPageOnly(call.Arg<int>(), reference.Items),
            });
        accountRequester
            .GetNpcs(Arg.Any<int>())
            .Returns(call => new NpcResponse
            {
                Data = FirstPageOnly(call.Arg<int>(), reference.Npcs),
            });
        accountRequester
            .GetNpcItems(Arg.Any<int>())
            .Returns(call => new NpcItemsResponse
            {
                Data = FirstPageOnly(call.Arg<int>(), reference.NpcItems),
            });
        accountRequester
            .GetResources(Arg.Any<int>())
            .Returns(call => new ResourceResponse
            {
                Data = FirstPageOnly(call.Arg<int>(), reference.Resources),
            });
        accountRequester
            .GetMonsters(Arg.Any<int>())
            .Returns(call => new MonstersResponse
            {
                Data = FirstPageOnly(call.Arg<int>(), reference.Monsters),
            });
        accountRequester
            .GetEvents(Arg.Any<int>())
            .Returns(call => new GetAllEventsResponse
            {
                Data = FirstPageOnly(call.Arg<int>(), reference.Events),
            });
        accountRequester
            .GetActiveEvents(Arg.Any<int>())
            .Returns(call => new GetActiveEventsResponse
            {
                Data = FirstPageOnly(call.Arg<int>(), reference.ActiveEvents),
            });

        accountRequester.GetTasks().Returns(reference.Tasks);
        accountRequester.GetTasksRewards().Returns(reference.TasksRewards);

        AccountRequester = accountRequester;
        ApiRequester = apiRequester;

        GameState gameState = new(accountRequester, apiRequester);

        gameState.Services.BankItemCache =
            bankItemCache ?? Substitute.For<BankItemCache>(accountRequester);

        Task.Run(async () =>
            {
                await gameState.LoadMaps();
                await gameState.LoadItems();
                await gameState.LoadNpcs();
                await gameState.LoadNpcItems();
                await gameState.LoadResources();
                await gameState.LoadMonsters();
                await gameState.LoadTasksList();
                await gameState.LoadTasksRewards();
                await gameState.Services.EventService.LoadEvents();
                await gameState.Services.EventService.LoadActiveEvents();
            })
            .Wait();

        GameState = gameState;

        return GameState;
    }

    private static List<T> FirstPageOnly<T>(int pageNumber, List<T> data) =>
        pageNumber == 1 ? data : [];

    private static ReferenceGameData LoadReferenceData() =>
        new(
            Items: TestDataLoader.Load<List<ItemSchema>>("items.json"),
            Resources: TestDataLoader.Load<List<ResourceSchema>>("resources.json"),
            Npcs: TestDataLoader.Load<List<NpcSchema>>("npcs.json"),
            Monsters: TestDataLoader.Load<List<MonsterSchema>>("monsters.json"),
            Maps: TestDataLoader.Load<List<MapSchema>>("maps.json"),
            NpcItems: TestDataLoader.Load<List<NpcItemSchema>>("npc_items.json"),
            Tasks: TestDataLoader.Load<List<TasksFullSchema>>("tasks.json"),
            TasksRewards: TestDataLoader.Load<List<DropRateSchema>>("tasks_rewards.json"),
            Events: TestDataLoader.Load<List<EventSchema>>("events.json"),
            ActiveEvents: TestDataLoader.Load<List<ActiveEventSchema>>("active_events.json")
        );

    private record ReferenceGameData(
        List<ItemSchema> Items,
        List<ResourceSchema> Resources,
        List<NpcSchema> Npcs,
        List<MonsterSchema> Monsters,
        List<MapSchema> Maps,
        List<NpcItemSchema> NpcItems,
        List<TasksFullSchema> Tasks,
        List<DropRateSchema> TasksRewards,
        List<EventSchema> Events,
        List<ActiveEventSchema> ActiveEvents
    );
}
