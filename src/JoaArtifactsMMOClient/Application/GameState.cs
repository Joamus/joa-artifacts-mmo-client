using Application.ArtifactsApi.Schemas;
using Application.ArtifactsApi.Schemas.Responses;
using Application.Character;
using Application.Services.ApiServices;
using Infrastructure;

namespace Application;

public class GameState
{
    readonly AccountRequester _accountRequester;
    readonly ApiRequester _apiRequester;

    ILogger _logger { get; init; }
    public List<PlayerCharacter> _characters { get; private set; }
    public List<ItemSchema> _items { get; set; }

    public List<MapSchema> _maps { get; set; }

    public List<ResourceSchema> _resources { get; set; }
    public List<NpcSchema> _npcs { get; set; }
    public List<MonsterSchema> _monsters { get; set; }

    private GameState(AccountRequester accountRequester, ApiRequester apiRequester)
    {
        _accountRequester = accountRequester;
        _apiRequester = apiRequester;
        _logger = LoggerFactory.Create(AppLogger.options).CreateLogger<GameState>();
    }

    public static async Task<GameState> LoadAll(
        AccountRequester accountRequester,
        ApiRequester apiRequester
    )
    {
        var gameState = new GameState(accountRequester, apiRequester);

        await gameState.LoadCharacters();
        await gameState.LoadItems();
        await gameState.LoadMaps();
        await gameState.LoadResources();
        await gameState.LoadMonsters();
        await gameState.LoadNpcs();

        return gameState;
    }

    public async Task LoadCharacters()
    {
        _logger.LogInformation("Loading characters...");
        var result = await _accountRequester.GetCharacters();

        List<PlayerCharacter> characters = [];

        foreach (var characterSchema in result.Data)
        {
            characters.Add(new PlayerCharacter(characterSchema, this, _apiRequester));
        }

        _characters = characters;
        _logger.LogInformation("Loading characters - DONE;");
    }

    public async Task LoadItems()
    {
        _logger.LogInformation("Loading items...");
        bool doneLoading = false;
        List<ItemSchema> items = [];
        int pageNumber = 1;

        while (!doneLoading)
        {
            var result = await _accountRequester.GetItems(pageNumber);

            foreach (var item in result.Data)
            {
                items.Add(item);
            }

            if (result.Data.Count == 0)
            {
                doneLoading = true;
            }

            pageNumber++;
        }
        _items = items;
        _logger.LogInformation("Loading items - DONE;");
    }

    public async Task LoadMaps()
    {
        _logger.LogInformation("Loading maps...");
        bool doneLoading = false;
        List<MapSchema> maps = [];
        int pageNumber = 1;

        while (!doneLoading)
        {
            var result = await _accountRequester.GetMaps(pageNumber);

            foreach (var map in result.Data)
            {
                maps.Add(map);
            }

            if (result.Data.Count == 0)
            {
                doneLoading = true;
            }

            pageNumber++;
        }
        _maps = maps;
        _logger.LogInformation("Loading maps - DONE;");
    }

    public async Task LoadResources()
    {
        _logger.LogInformation("Loading resources...");
        bool doneLoading = false;
        List<ResourceSchema> resources = [];
        int pageNumber = 1;

        while (!doneLoading)
        {
            var result = await _accountRequester.GetResources(pageNumber);

            foreach (var resource in result.Data)
            {
                resources.Add(resource);
            }

            if (result.Data.Count == 0)
            {
                doneLoading = true;
            }

            pageNumber++;
        }

        _resources = resources;
        _logger.LogInformation("Loading resources - DONE;");
    }

    public async Task LoadNpcs()
    {
        _logger.LogInformation("Loading NPCs...");
        bool doneLoading = false;
        List<NpcSchema> npcs = [];
        int pageNumber = 1;

        while (!doneLoading)
        {
            var result = await _accountRequester.GetNpcs(pageNumber);

            foreach (var npc in result.Data)
            {
                npcs.Add(npc);
            }

            if (result.Data.Count == 0)
            {
                doneLoading = true;
            }

            pageNumber++;
        }
        _npcs = npcs;
        _logger.LogInformation("Loading NPCs - DONE;");
    }

    public async Task LoadMonsters()
    {
        _logger.LogInformation("Loading monsters...");
        bool doneLoading = false;
        List<MonsterSchema> monsters = [];
        int pageNumber = 1;

        while (!doneLoading)
        {
            var result = await _accountRequester.GetMonsters(pageNumber);

            foreach (var monster in result.Data)
            {
                monsters.Add(monster);
            }

            if (result.Data.Count == 0)
            {
                doneLoading = true;
            }

            pageNumber++;
        }
        _monsters = monsters;
        _logger.LogInformation("Loading monsters - DONE;");
    }
}
