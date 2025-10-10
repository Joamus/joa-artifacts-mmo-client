using Application.ArtifactsApi.Schemas;
using Application.ArtifactsApi.Schemas.Responses;
using Application.Character;
using Application.Services;
using Application.Services.ApiServices;
using Infrastructure;

namespace Application;

public class GameState
{
    public AccountRequester AccountRequester { get; init; }
    readonly ApiRequester _apiRequester;

    DateTime cacheReload = DateTime.UtcNow;

    ILogger logger { get; init; }

    public List<PlayerCharacter> Characters { get; private set; } = [];
    public List<ItemSchema> Items { get; set; } = [];
    public Dictionary<string, ItemSchema> ItemsDict { get; set; } = [];

    public Dictionary<string, ItemSchema> UtilityItemsDict { get; set; } = [];

    public Dictionary<string, List<ItemSchema>> CraftingLookupDict { get; set; } = [];

    public List<NpcItemSchema> NpcItems { get; set; } = [];
    public Dictionary<string, NpcItemSchema> NpcItemsDict { get; set; } = [];

    public List<MapSchema> Maps { get; set; } = [];

    public List<ResourceSchema> Resources { get; set; } = [];
    public List<NpcSchema> Npcs { get; set; } = [];
    public List<AccountAchievementSchema> AccountAchievements { get; set; } = [];
    public List<MonsterSchema> Monsters { get; set; } = [];

    public GameState(AccountRequester accountRequester, ApiRequester apiRequester)
    {
        AccountRequester = accountRequester;
        _apiRequester = apiRequester;
        logger = AppLogger.loggerFactory.CreateLogger<GameState>();
    }

    public async Task LoadAll()
    {
        cacheReload = DateTime.UtcNow;

        await LoadCharacters();
        await LoadItems();
        await LoadNpcItems();
        await LoadMaps();
        await LoadResources();
        await LoadMonsters();
        await LoadNpcs();
        await LoadAccountAchievements();
    }

    public bool ShouldReload()
    {
        DateTime now = DateTime.UtcNow;
        double secondsDiff = (now - cacheReload).TotalSeconds;
        return secondsDiff > 60 * 10;
    }

    public async Task ReloadAll()
    {
        // Don't load characters, they are probably being mutated, so the data might be out of date when they are updated
        cacheReload = DateTime.UtcNow;

        // await LoadItems();
        // await LoadNpcItems();
        // await LoadMaps();
        // await LoadResources();
        // await LoadMonsters();
        // await LoadNpcs();
        // Just reload achievements for now, for things that are limited by achievements
        await LoadAccountAchievements();
    }

    public async Task LoadCharacters()
    {
        logger.LogInformation("Loading characters...");
        var result = await AccountRequester.GetCharacters();

        List<PlayerCharacter> characters = [];

        foreach (var characterSchema in result.Data)
        {
            characters.Add(new PlayerCharacter(characterSchema));
        }

        Characters = characters;
        logger.LogInformation("Loading characters - DONE;");
    }

    public async Task LoadItems()
    {
        logger.LogInformation("Loading items...");
        bool doneLoading = false;
        List<ItemSchema> items = [];
        Dictionary<string, ItemSchema> itemsDict = new();
        Dictionary<string, ItemSchema> utilityItemsDict = new();
        Dictionary<string, List<ItemSchema>> craftingLookupDict = new();

        int pageNumber = 1;

        while (!doneLoading)
        {
            var result = await AccountRequester.GetItems(pageNumber);

            foreach (var item in result.Data)
            {
                items.Add(item);
                itemsDict.Add(item.Code, item);

                if (item.Type == "utility")
                {
                    utilityItemsDict.Add(item.Code, item);
                }

                if (item.Craft is not null)
                {
                    foreach (var ingredient in item.Craft.Items)
                    {
                        if (!craftingLookupDict.ContainsKey(ingredient.Code))
                        {
                            craftingLookupDict.Add(ingredient.Code, []);
                        }
                        craftingLookupDict[ingredient.Code].Add(item);
                    }
                }
            }

            if (result.Data.Count == 0)
            {
                doneLoading = true;
            }

            pageNumber++;
        }
        Items = items;
        ItemsDict = itemsDict;
        UtilityItemsDict = utilityItemsDict;
        CraftingLookupDict = craftingLookupDict;

        logger.LogInformation("Loading items - DONE;");
    }

    public async Task LoadNpcItems()
    {
        logger.LogInformation("Loading NPC items...");
        bool doneLoading = false;
        List<NpcItemSchema> items = [];
        Dictionary<string, NpcItemSchema> itemsDict = new();
        Dictionary<string, List<NpcItemSchema>> craftingLookupDict = new();

        int pageNumber = 1;

        while (!doneLoading)
        {
            var result = await AccountRequester.GetNpcItems(pageNumber);

            foreach (var item in result.Data)
            {
                items.Add(item);
                itemsDict.Add(item.Code, item);
            }

            if (result.Data.Count == 0)
            {
                doneLoading = true;
            }

            pageNumber++;
        }
        NpcItems = items;
        NpcItemsDict = itemsDict;

        logger.LogInformation("Loading NPC items - DONE;");
    }

    public async Task LoadMaps()
    {
        logger.LogInformation("Loading maps...");
        bool doneLoading = false;
        List<MapSchema> maps = [];
        int pageNumber = 1;

        while (!doneLoading)
        {
            var result = await AccountRequester.GetMaps(pageNumber);

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
        Maps = maps;
        logger.LogInformation("Loading maps - DONE;");
    }

    public async Task LoadResources()
    {
        logger.LogInformation("Loading resources...");
        bool doneLoading = false;
        List<ResourceSchema> resources = [];
        int pageNumber = 1;

        while (!doneLoading)
        {
            var result = await AccountRequester.GetResources(pageNumber);

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

        Resources = resources;
        logger.LogInformation("Loading resources - DONE;");
    }

    public async Task LoadNpcs()
    {
        logger.LogInformation("Loading NPCs...");
        bool doneLoading = false;
        List<NpcSchema> npcs = [];
        int pageNumber = 1;

        while (!doneLoading)
        {
            var result = await AccountRequester.GetNpcs(pageNumber);

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
        Npcs = npcs;
        logger.LogInformation("Loading NPCs - DONE;");
    }

    public async Task LoadAccountAchievements()
    {
        logger.LogInformation("Loading account achievements...");
        bool doneLoading = false;
        List<AccountAchievementSchema> accountAchievements = [];
        int pageNumber = 1;

        try
        {
            while (!doneLoading)
            {
                var result = await AccountRequester.GetAccountAchievements(pageNumber);

                foreach (var achievement in result.Data)
                {
                    if (!string.IsNullOrEmpty(achievement.CompletedAt))
                    {
                        accountAchievements.Add(achievement);
                    }
                }

                if (result.Data.Count == 0)
                {
                    doneLoading = true;
                }

                pageNumber++;
            }
            AccountAchievements = accountAchievements;
            logger.LogInformation("Loading account achievements - DONE;");
        }
        catch (Exception e)
        {
            logger.LogError(e.ToString());
        }
    }

    public async Task LoadMonsters()
    {
        logger.LogInformation("Loading monsters...");
        bool doneLoading = false;
        List<MonsterSchema> monsters = [];
        int pageNumber = 1;

        while (!doneLoading)
        {
            var result = await AccountRequester.GetMonsters(pageNumber);

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
        Monsters = monsters;
        logger.LogInformation("Loading monsters - DONE;");
    }
}
