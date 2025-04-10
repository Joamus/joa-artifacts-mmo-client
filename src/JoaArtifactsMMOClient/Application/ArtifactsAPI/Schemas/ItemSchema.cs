using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using Application.Artifacts.Schemas;
using Application.ArtifactsApi.Schemas;
using Newtonsoft.Json.Converters;

namespace Application.ArtifactsApi.Schemas;

public record ItemSchema
{
    public string Name { get; set; }

    public string Code { get; set; }

    // This is level required to equip AND level required to craft it through a skill
    public int Level { get; set; }

    public string Type { get; set; }

    public string Subtype { get; set; }

    public string Description { get; set; }

    public List<SimpleEffectSchema> Effects { get; set; } = [];

    public CraftDto? Craft { get; set; }

    public bool Tradeable { get; set; }
}

public enum ItemType
{
    [EnumMember(Value = "consumable")]
    Consumable,

    [EnumMember(Value = "weapon")]
    Weapon,

    [EnumMember(Value = "resource")]
    Resource,
}

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
    public Skill Skill { get; set; }

    [JsonPropertyName("level")]
    public int Level { get; set; }

    [JsonPropertyName("items")]
    public List<DropSchema> Items { get; set; } = [];

    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }
}
