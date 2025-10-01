using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace Application.ArtifactsApi.Schemas;

public record MapContentSchema
{
    // public ContentType? Type { get; set; } = null;
    public string? Type { get; set; } = null;

    public string Code { get; set; } = "";
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
