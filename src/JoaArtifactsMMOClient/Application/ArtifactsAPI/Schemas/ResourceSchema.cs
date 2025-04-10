using System.Text.Json.Serialization;
using Application.Artifacts.Schemas;

namespace Application.ArtifactsApi.Schemas.Responses;

public record ResourceSchema
{
    public string Name { get; set; } = "";

    public string Code { get; set; } = "";

    // public Skill Skill;
    public string Skill { get; set; } = "";

    public int Level { get; set; }

    public List<DropRateSchema> Drops { get; set; } = [];
}
