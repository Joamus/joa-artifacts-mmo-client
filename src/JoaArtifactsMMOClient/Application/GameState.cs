using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using Application.ArtifactsApi.Schemas;
using Application.ArtifactsApi.Schemas.Responses;
using Application.Character;
using Application.Dtos;
using Application.Errors;
using Application.Services;
using Application.Services.ApiServices;
using Infrastructure;

namespace Application;

public class GameState
{
    public AccountRequester AccountRequester { get; init; }
    readonly ApiRequester apiRequester;

    DateTime cacheReload = DateTime.UtcNow;
    public DateTime PendingItemClaimEvaluation { get; set; } = DateTime.UtcNow;

    ILogger logger { get; init; }

    public required BankItemCache BankItemCache { get; set; }

    public List<PlayerCharacter> Characters { get; private set; } = [];
    public List<PlayerAI> CharacterAIs { get; private set; } = [];
    public List<ItemSchema> Items { get; set; } = [];
    List<PendingItemSchema> PendingItems { get; set; } = [];

    public bool ShouldUpdatePendingItems { get; set; } = false;

    public List<TasksFullSchema> Tasks { get; set; } = [];

    public CharacterChoreService ChoreService { get; set; }
    public Dictionary<string, ItemSchema> ItemsDict { get; set; } = [];

    public Dictionary<string, ItemSchema> UtilityItemsDict { get; set; } = [];

    public Dictionary<string, List<ItemSchema>> CraftingLookupDict { get; set; } = [];
    public Dictionary<
        string,
        List<(ResourceSchema Resource, DropRateSchema Drop)>
    > DropItemsDict { get; set; } = [];

    public List<NpcItemSchema> NpcItems { get; set; } = [];
    public Dictionary<string, NpcItemSchema> NpcItemsDict { get; set; } = [];

    public List<MapSchema> Maps { get; set; } = [];

    public Dictionary<int, MapSchema> MapsDict { get; set; } = [];
    public List<ResourceSchema> Resources { get; set; } = [];
    public List<NpcSchema> Npcs { get; set; } = [];
    public List<NpcSchema> AvailableNpcs { get; set; } = [];
    public List<AccountAchievementSchema> AccountAchievements { get; set; } = [];
    public List<MonsterSchema> Monsters { get; set; } = [];
    public Dictionary<string, MonsterSchema> MonstersDict { get; set; } = [];
    public List<MonsterSchema> AvailableMonsters { get; set; } = [];
    public Dictionary<string, MonsterSchema> AvailableMonstersDict { get; set; } = [];

    public EventService EventService { get; set; }

    [SetsRequiredMembers]
    public GameState(AccountRequester accountRequester, ApiRequester apiRequester)
    {
        AccountRequester = accountRequester;
        this.apiRequester = apiRequester;
        logger = AppLogger.loggerFactory.CreateLogger<GameState>();
        BankItemCache = new BankItemCache(accountRequester);
        EventService = new EventService(
            AppLogger.loggerFactory.CreateLogger<EventService>(),
            accountRequester,
            this
        );

        ChoreService = new CharacterChoreService();
    }

    public async Task LoadAll(List<CharacterConfig> characterConfigs)
    {
        cacheReload = DateTime.UtcNow;

        await LoadMaps();
        await LoadItems();
        await LoadNpcs();
        await LoadNpcItems();
        await LoadResources();
        await LoadMonsters();
        await LoadPendingItems();
        await LoadAccountAchievements();
        await LoadTasksList();
        await BankItemCache.GetBankItems(null);
        await EventService.LoadEvents();
        await EventService.LoadActiveEvents();
        await LoadCharacters(characterConfigs);
    }

    public bool ShouldReload()
    {
        DateTime now = DateTime.UtcNow;
        double secondsDiff = (now - cacheReload).TotalSeconds;
        return secondsDiff > 60 * 5;
    }

    public bool ShouldClaimPendingItems()
    {
        DateTime now = DateTime.UtcNow;
        double secondsDiff = (now - PendingItemClaimEvaluation).TotalSeconds;
        return secondsDiff > 60 * 5;
    }

    public async Task ReloadAll()
    {
        // Don't load characters, they are probably being mutated, so the data might be out of date when they are updated
        cacheReload = DateTime.UtcNow;

        // Just reload achievements for now, for things that are limited by achievements
        await LoadAccountAchievements();
        await LoadMaps();
        AvailableMonsters = GetAvailableMonsters(Monsters);
        AvailableMonstersDict = Monsters.ToDictionary(monster => monster.Code);
        AvailableNpcs = GetAvailableNpcs(Npcs);

        await EventService.LoadActiveEvents();
        // Loading these for when events update
    }

    public async Task LoadCharacters(List<CharacterConfig> characterConfigs)
    {
        // Bind the "Characters" section to a list of CharacterConfig
        logger.LogInformation("Loading characters...");
        var result = await AccountRequester.GetCharacters();

        List<PlayerCharacter> characters = [];
        List<PlayerAI> characterAIs = [];

        foreach (var characterSchema in result.Data)
        {
            var matchingConfig = characterConfigs.FirstOrDefault(config =>
                config.Name == characterSchema.Name
            );
            if (matchingConfig is null)
            {
                throw new AppError($"Could not find character config for ${characterSchema.Name}");
            }
            var character = new PlayerCharacter(
                characterSchema,
                this,
                apiRequester,
                matchingConfig
            );
            characters.Add(character);
            characterAIs.Add(new PlayerAI(character, this, matchingConfig?.AI ?? true));
        }

        Characters = characters;
        CharacterAIs = characterAIs;
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

    public async Task LoadTasksList()
    {
        logger.LogInformation("Loading tasks list...");
        List<TasksFullSchema> tasks = [];

        var result = await AccountRequester.GetTasks();

        foreach (var task in result)
        {
            tasks.Add(task);
        }

        Tasks = tasks;

        logger.LogInformation("Loading tasks list - DONE;");
    }

    public async Task LoadMaps()
    {
        logger.LogInformation("Loading maps...");
        bool doneLoading = false;
        List<MapSchema> maps = [];
        Dictionary<int, MapSchema> mapsDict = [];
        int pageNumber = 1;

        while (!doneLoading)
        {
            var result = await AccountRequester.GetMaps(pageNumber);

            foreach (var map in result.Data)
            {
                maps.Add(map);
                mapsDict.Add(map.MapId, map);
            }

            if (result.Data.Count == 0)
            {
                doneLoading = true;
            }

            pageNumber++;
        }
        Maps = maps;
        MapsDict = mapsDict;
        logger.LogInformation("Loading maps - DONE;");
    }

    public async Task LoadResources()
    {
        logger.LogInformation("Loading resources...");
        bool doneLoading = false;
        List<ResourceSchema> resources = [];
        int pageNumber = 1;

        Dictionary<string, List<(ResourceSchema Resource, DropRateSchema Drop)>> dropItemsDict = [];

        while (!doneLoading)
        {
            var result = await AccountRequester.GetResources(pageNumber);

            foreach (var resource in result.Data)
            {
                resources.Add(resource);

                foreach (var drop in resource.Drops)
                {
                    if (!dropItemsDict.ContainsKey(drop.Code))
                    {
                        dropItemsDict.Add(drop.Code, []);
                    }

                    dropItemsDict[drop.Code].Add((resource, drop));
                }
            }

            if (result.Data.Count == 0)
            {
                doneLoading = true;
            }

            pageNumber++;
        }

        Resources = resources;
        DropItemsDict = dropItemsDict;
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
        AvailableNpcs = GetAvailableNpcs(npcs);
        logger.LogInformation("Loading NPCs - DONE;");
    }

    private List<NpcSchema> GetAvailableNpcs(List<NpcSchema> npcs)
    {
        return
        [
            .. npcs.Where(npc =>
                Maps.Exists(map =>
                    map.Interactions.Content?.Code == npc.Code
                    && !NavigationService.UnavailableIslands.Contains(map.Name)
                )
            ),
        ];
    }

    private List<MonsterSchema> GetAvailableMonsters(List<MonsterSchema> monsters)
    {
        return
        [
            .. monsters.Where(monster =>
                Maps.Exists(map =>
                    map.Interactions.Content?.Code == monster.Code
                    && !NavigationService.UnavailableIslands.Contains(map.Name)
                )
            ),
        ];
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
        Dictionary<string, MonsterSchema> monstersDict = [];
        int pageNumber = 1;

        while (!doneLoading)
        {
            var result = await AccountRequester.GetMonsters(pageNumber);

            foreach (var monster in result.Data)
            {
                monster.MaxHp = monster.Hp;
                monsters.Add(monster);
                monstersDict.Add(monster.Code, monster);
            }

            if (result.Data.Count == 0)
            {
                doneLoading = true;
            }

            pageNumber++;
        }

        Monsters = monsters;
        MonstersDict = monstersDict;
        AvailableMonsters = GetAvailableMonsters(monsters);
        AvailableMonstersDict = AvailableMonsters.ToDictionary(monster => monster.Code);
        logger.LogInformation("Loading monsters - DONE;");
    }

    async Task LoadPendingItems()
    {
        logger.LogInformation("Loading pending items...");
        bool doneLoading = false;
        List<PendingItemSchema> pendingItems = [];
        int pageNumber = 1;

        while (!doneLoading)
        {
            var result = await AccountRequester.GetPendingItems(pageNumber);

            foreach (var pendingItem in result.Data)
            {
                pendingItems.Add(pendingItem);
            }

            if (result.Data.Count == 0)
            {
                doneLoading = true;
            }

            pageNumber++;
        }

        PendingItems = pendingItems;
        logger.LogInformation("Loading pending items - DONE;");
    }

    public async Task<ReadOnlyCollection<PendingItemSchema>> GetPendingItems()
    {
        if (ShouldUpdatePendingItems)
        {
            await LoadPendingItems();
        }

        return PendingItems.AsReadOnly();
    }

    public async Task<int> GetAmountOfItemFromAll(string itemCode)
    {
        int amountOnCharacters = Characters.Sum(character =>
        {
            var equippedOrInventoryItem = character.GetEquippedItemOrInInventory(itemCode);

            return equippedOrInventoryItem.Sum(equippedOrInventory =>
                equippedOrInventory.equipmentSlot.Quantity
            );
        });

        var bankResponse = await BankItemCache.GetBankItems(Characters.First(), false);

        int amountInBank = bankResponse.Sum(bankItem =>
            bankItem.Code == itemCode ? bankItem.Quantity : 0
        );

        return amountOnCharacters + amountInBank;
    }

    public static LevelRange GetCharacterLevelRange(GameState gameState)
    {
        List<int> characterLevels = [.. gameState.Characters.Select((x) => x.Schema.Level)];
        characterLevels.Sort((a, b) => a - b);

        return new LevelRange
        {
            Lowest = characterLevels.First(),
            Highest = characterLevels.Last(),
        };
    }
}
