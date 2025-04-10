using System.Text.Json.Serialization;

namespace Application.ArtifactsApi.Schemas;
public record MapSchema
{
    public string Name { get; set; } = "";

    public string Skin { get; set; } = "";

    public int X { get; set; }

    public int Y { get; set; }

    public MapContentSchema? Content { get; set; } = null;
}
