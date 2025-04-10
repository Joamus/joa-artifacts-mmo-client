using System.Text.Json.Serialization;

namespace Application.ArtifactsApi.Schemas;

public record SkillInfoSchema
{
    [JsonPropertyName("xp")]
    public int Xp { get; set; }

    [JsonPropertyName("items")]
    public List<DropSchema> Items { get; set; } = [];
}
