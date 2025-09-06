using System.Text.Json.Serialization;

namespace Application.ArtifactsApi.Schemas;

public record SkillInfoSchema
{
    public int Xp { get; set; }

    public List<DropSchema> Items { get; set; } = [];
}
