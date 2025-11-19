using Application.ArtifactsApi.Schemas;
using Application.Services.ApiServices;
using OneOf.Types;

namespace Application.Services;

public class EventService
{
    const int ACTIVE_EVENT_EXPIRATION_BUFFER_SECONDS = 60;
    public AccountRequester accountRequester { get; init; }

    private const string Name = "EventService";

    private readonly ILogger<EventService> logger;

    private GameState gameState { get; set; }
    public List<EventSchema> Events { get; private set; } = [];
    public Dictionary<string, EventSchema> EventsDict { get; private set; } = [];
    public Dictionary<string, EventSchema> EventEntitiesDict { get; private set; } = [];
    private List<ActiveEventSchema> _activeEvents = [];
    public List<ActiveEventSchema> ActiveEvents
    {
        get
        {
            return _activeEvents
                .Where(_event =>
                    _event.Expiration
                    > DateTime.UtcNow.AddSeconds(ACTIVE_EVENT_EXPIRATION_BUFFER_SECONDS)
                )
                .ToList();
        }
        private set { _activeEvents = value; }
    }

    public EventService(
        ILogger<EventService> logger,
        AccountRequester accountRequester,
        GameState gameState
    )
    {
        this.logger = logger;
        this.accountRequester = accountRequester;
        this.gameState = gameState;
    }

    public async Task LoadEvents()
    {
        logger.LogInformation("Loading events");
        bool doneLoading = false;
        List<EventSchema> events = [];
        Dictionary<string, EventSchema> eventsDict = [];
        Dictionary<string, EventSchema> eventEntitiesDict = [];
        int pageNumber = 1;

        try
        {
            while (!doneLoading)
            {
                var result = await accountRequester.GetEvents(pageNumber);

                foreach (var gameEvent in result.Data)
                {
                    events.Add(gameEvent);
                    eventsDict.Add(gameEvent.Code, gameEvent);

                    // var existingEventEntity = eventEntitiesDict.GetValueOrNull(
                    //     gameEvent.Content.Code
                    // );

                    eventEntitiesDict.Add(gameEvent.Content.Code, gameEvent);

                    // if (existingEventEntity is not null)
                    // {
                    // 	existingEventEntity
                    // }
                }

                if (result.Data.Count == 0)
                {
                    doneLoading = true;
                }

                pageNumber++;
            }
            Events = events;
            EventsDict = eventsDict;
            EventEntitiesDict = eventEntitiesDict;
            logger.LogInformation("Loading events - DONE;");
        }
        catch (Exception e)
        {
            logger.LogError(e.ToString());
        }
    }

    public async Task LoadActiveEvents()
    {
        logger.LogInformation("Loading active events");
        bool doneLoading = false;
        List<ActiveEventSchema> activeEvents = [];
        int pageNumber = 1;

        try
        {
            while (!doneLoading)
            {
                var result = await accountRequester.GetActiveEvents(pageNumber);

                foreach (var _event in result.Data)
                {
                    activeEvents.Add(_event);
                }

                if (result.Data.Count == 0)
                {
                    doneLoading = true;
                }

                pageNumber++;
            }
            ActiveEvents = activeEvents;
            logger.LogInformation("Loading active events - DONE;");
        }
        catch (Exception e)
        {
            logger.LogError(e.ToString());
        }
    }

    public MapSchema? WhereIsEntityActive(string code)
    {
        var gameEvent = EventEntitiesDict.GetValueOrNull(code);

        if (gameEvent is null)
        {
            return null;
        }

        var activeEvent = ActiveEvents.FirstOrDefault(activeEvent =>
            activeEvent.Code == gameEvent.Code
        );

        if (activeEvent is null)
        {
            return null;
        }

        return activeEvent.Map;
    }

    public bool IsEntityFromEvent(string code)
    {
        return EventEntitiesDict.GetValueOrNull(code) is not null;
    }

    public bool IsItemFromEventMonster(string code, bool mustBeActive)
    {
        var monstersThatDropTheItem = gameState.Monsters.FindAll(monster =>
            monster.Drops.Find(drop => drop.Code == code) is not null
        );

        if (monstersThatDropTheItem.Count > 0)
        {
            return false;
        }

        foreach (var monster in monstersThatDropTheItem)
        {
            var monsterIsFromEvent = IsEntityFromEvent(monster.Code);

            if (monsterIsFromEvent && mustBeActive && WhereIsEntityActive(monster.Code) is not null)
            {
                return false;
            }

            if (!monsterIsFromEvent)
            {
                return false;
            }
        }

        return true;
    }
}
