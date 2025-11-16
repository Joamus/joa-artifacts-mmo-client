namespace Application.ArtifactsApi.Schemas;

public record MapContentSchema
{
    // public ContentType? Type { get; set; } = null;
    public required ContentType Type { get; set; }

    public required string Code { get; set; }
}

public enum ContentType
{
    Monster,

    Resource,

    Workshop,

    Bank,

    GrandExchange,

    TasksMaster,

    Npc,
    TasksTrader,
}
