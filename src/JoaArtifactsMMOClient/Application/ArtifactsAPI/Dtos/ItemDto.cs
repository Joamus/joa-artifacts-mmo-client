using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using Applcation.ArtifactsAPI.Dtos;
using Newtonsoft.Json.Converters;

namespace Application.ArtifactsAPI.Responses;

public record ItemDto
{
    [JsonPropertyName("name")]
    string Name;

    [JsonPropertyName("code")]
    string Code;

    [JsonPropertyName("level")]
    // This is item level, not lvl required to equip
    int Level;

    [JsonPropertyName("type")]
    ItemType Type;

    [JsonPropertyName("subtype")]
    ItemType SubType;

    [JsonPropertyName("description")]
    string Description;

    [JsonPropertyName("effects")]
    List<EffectDto> effects = [];

    [JsonPropertyName("craft")]
    CraftDto? Craft;

    [JsonPropertyName("tradeable")]
    bool Tradeable;
}

[JsonConverter(typeof(StringEnumConverter))]
public enum ItemType
{
    [EnumMember(Value = "consumable")]
    Consumable,

    [EnumMember(Value = "weapon")]
    Weapon,

    [EnumMember(Value = "resource")]
    Resource,
}

[JsonConverter(typeof(StringEnumConverter))]
public enum ItemSubType
{
    [EnumMember(Value = "")]
    None,

    [EnumMember(Value = "food")]
    Food,

    [EnumMember(Value = "mining")]
    Mining,
}

public record CraftDto
{
    [JsonPropertyName("skill")]
    Skill Skill;

    [JsonPropertyName("level")]
    int Level;

    [JsonPropertyName("items")]
    List<DropDto> Items = [];

    [JsonPropertyName("quantity")]
    int Quantity;
}
