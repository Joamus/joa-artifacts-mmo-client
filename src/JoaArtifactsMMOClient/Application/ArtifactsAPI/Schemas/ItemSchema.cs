using System.Runtime.Serialization;
using Application.Artifacts.Schemas;

namespace Application.ArtifactsApi.Schemas;

public record ItemSchema
{
    public required string Name { get; set; } = "";

    public required string Code { get; set; } = "";

    // This is level required to equip AND level required to craft it through a skill
    public int Level { get; set; }

    public required string Type { get; set; } = "";

    public required string Subtype { get; set; } = "";

    public required string Description { get; set; } = "";

    public List<SimpleEffectSchema> Effects { get; set; } = [];

    public List<ItemOrMapCondition>? Conditions { get; set; } = [];

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

public record ItemOrMapCondition
{
    public string Code { get; set; } = "";

    public ItemConditionOperator Operator { get; set; }

    public int Value { get; set; }
}

public enum ItemConditionOperator
{
    Eq,
    Ne,

    Gt,
    Lt,
    Cost,
    HasItem,
    AchievementUnlocked,
}
