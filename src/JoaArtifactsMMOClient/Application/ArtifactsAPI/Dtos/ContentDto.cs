using System.Net.Mime;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

public record ContentDto
{
    [JsonPropertyName("type")]
    ContentType Type;

    [JsonPropertyName("code")]
    string Code;
}

public enum ContentType
{
    [EnumMember(Value = "monster")]
    Monster,

    [EnumMember(Value = "resource")]
    Resource,

    [EnumMember(Value = "workshop")]
    Workshop,

    [EnumMember(Value = "bank")]
    Bank,

    [EnumMember(Value = "grand_exchange")]
    GrandExchange,

    [EnumMember(Value = "tasks_master")]
    TasksMaster,

    [EnumMember(Value = "npc")]
    Npc,
}
