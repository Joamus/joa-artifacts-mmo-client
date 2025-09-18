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

    public List<ItemCondition> Conditions { get; set; } = [];

    public CraftDto? Craft { get; set; }

    public bool Tradeable { get; set; }
}

public enum ItemType
{
    Consumable,

    Weapon,
    Shield,
    BodyArmor,
    LegArmor,
    Ring,
    Amulet,
    Artifact,
    Rune,
    Bag,
    Utility,

    Resource,
}

public enum ItemSubType
{
    [EnumMember(Value = "")]
    None,

    Food,

    Mining,
    Woodcutting,
}

public record CraftDto
{
    public Skill Skill { get; set; }

    public int Level { get; set; }

    public List<DropSchema> Items { get; set; } = [];

    public int Quantity { get; set; }
}

public record ItemCondition
{
    public string Code { get; set; } = "";

    public string Operator { get; set; } = "";

    public int Value { get; set; }
}

public static class ItemConditionOperator
{
    public static readonly string GreaterThan = "gt";

    // Haven't seen this one yet
    public readonly static string LessThan = "lt";
}
