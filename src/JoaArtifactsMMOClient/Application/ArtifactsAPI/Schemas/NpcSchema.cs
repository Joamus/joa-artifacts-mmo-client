using System.Text.Json.Serialization;

namespace Application.ArtifactsApi.Schemas;

public record NpcSchema
{
    public required string Name { get; set; } = "";

    public required string Code { get; set; } = "";

    public required string Description { get; set; } = "";

    // Allowed value is merchant;
    public required string Type { get; set; } = "";
}
