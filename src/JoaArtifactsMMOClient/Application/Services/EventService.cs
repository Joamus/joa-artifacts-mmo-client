using Application.ArtifactsApi.Schemas;
using Application.Services.ApiServices;

namespace Application.Services;

public class EventService
{
    const int ACTIVE_EVENT_EXPIRATION_BUFFER_SECONDS = 60;
    public AccountRequester accountRequester { get; init; }

    private const string Name = "EventService";

    private readonly ILogger<EventService> logger;

    public List<EventSchema> Events { get; private set; } = [];
    public Dictionary<string, EventSchema> EventsDict { get; private set; } = [];
    private List<ActiveEventSchema> _activeEvents = [];
    public List<ActiveEventSchema> ActiveEvents
    {
        get
        {
            var now = DateTime.UtcNow;

            return _activeEvents
                .Where(_event =>
                    (_event.Expiration - DateTime.UtcNow).TotalSeconds
                    >= ACTIVE_EVENT_EXPIRATION_BUFFER_SECONDS
                )
                .ToList();
        }
        private set { _activeEvents = value; }
    }

    public EventService(ILogger<EventService> logger, AccountRequester accountRequester)
    {
        this.logger = logger;
        this.accountRequester = accountRequester;
    }

    public async Task LoadEvents()
    {
        logger.LogInformation("Loading events");
        bool doneLoading = false;
        List<EventSchema> events = [];
        Dictionary<string, EventSchema> eventsDict = [];
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
                }

                if (result.Data.Count == 0)
                {
                    doneLoading = true;
                }

                pageNumber++;
            }
            Events = events;
            EventsDict = eventsDict;
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

    public MapSchema? WhereIsMonsterActive()
    {
        return null;
    }

    public MapSchema? WhereIsResourceActive()
    {
        return null;
    }

    public MapSchema? WhereIsNpcActive()
    {
        return null;
    }
}
