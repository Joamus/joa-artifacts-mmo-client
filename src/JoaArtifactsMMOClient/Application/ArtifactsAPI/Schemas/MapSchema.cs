using System.Diagnostics;
using System.Security.AccessControl;
using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace Application.ArtifactsApi.Schemas;

public record MapSchema
{
    public string Name { get; set; } = "";

    public string Skin { get; set; } = "";

    public int X { get; set; }

    public int Y { get; set; }

    public required MapLayer Layer { get; set; }

    public required MapInteractions Interactions { get; set; }

    public required AccessSchema Access { get; set; }
}

public record AccessSchema
{
    public required AccessType Type { get; set; }
    public required List<ItemOrMapCondition>? Conditions { get; set; } = [];
}

public enum AccessType
{
    Standard,
    Teleportation,
    Conditional,
    Blocked,
}

public record MapInteractions
{
    public MapContentSchema? Content { get; set; } = null;
    public MapTransition? Transition { get; set; } = null;
}

public record MapTransition
{
    public required int MapId { get; set; }
    public required int X { get; set; }
    public required int Y { get; set; }
    public required MapLayer Layer { get; set; }

    public required List<ItemOrMapCondition> Conditions = [];
}

public enum MapLayer
{
    Interior,
    Overworld,
    Underground,
}
