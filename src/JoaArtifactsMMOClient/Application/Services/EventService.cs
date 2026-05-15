using Application.ArtifactsApi.Schemas;
using Application.Services.ApiServices;

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
                    eventEntitiesDict.Add(gameEvent.Content.Code, gameEvent);
                }

                if (result.Data.Count == 0)
                {
                    doneLoading = true;
                }

                pageNumber++;
            }
        }
        catch (Exception e)
        {
            logger.LogError(e.ToString());
        }

        Events = events;
        EventsDict = eventsDict;
        EventEntitiesDict = eventEntitiesDict;

        logger.LogInformation("Loading events - DONE;");
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
        }
        catch (Exception e)
        {
            logger.LogError(e.ToString());
        }

        bool eventsHaveChanged = EventListsAreDifferent(ActiveEvents, activeEvents);
        ActiveEvents = activeEvents;

        logger.LogInformation($"Loading active events - events have changed - {eventsHaveChanged}");

        if (eventsHaveChanged)
        {
            await NotifyCharactersOnEventChange();
        }

        logger.LogInformation("Loading active events - DONE");
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

    async Task NotifyCharactersOnEventChange()
    {
        logger.LogInformation(
            $"New active events have been detected - notifying character AIs to evaluate new events"
        );

        foreach (var characterAi in gameState.CharacterAIs.Where(ai => ai.Enabled))
        {
            await characterAi.EvaluateEventsChanged();
        }
    }

    public bool IsItemFromEventMonster(string code, bool mustBeActive)
    {
        var monstersThatDropTheItem = gameState.AvailableMonsters.FindAll(monster =>
            monster.Drops.Find(drop => drop.Code == code) is not null
        );

        if (monstersThatDropTheItem.Count == 0)
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

    public static bool EventListsAreDifferent(
        List<ActiveEventSchema> oldEvents,
        List<ActiveEventSchema> newEvents
    )
    {
        if (oldEvents.Count != newEvents.Count)
        {
            return true;
        }

        if (
            !oldEvents.Exists(oldEvent =>
                newEvents.Exists(newEvent => oldEvent.Code == newEvent.Code)
            )
        )
        {
            return true;
        }

        if (
            !newEvents.Exists(newEvent =>
                oldEvents.Exists(oldEvent => newEvent.Code == oldEvent.Code)
            )
        )
        {
            return true;
        }

        return false;
    }
}
