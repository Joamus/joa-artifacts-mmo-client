using System.Text.Json.Serialization;

namespace Applcation.ArtifactsAPI.Dtos;

public record GatherDto
{
    [JsonPropertyName("xp")]
    int Xp;

    [JsonPropertyName("items")]
    List<DropDto> Items = [];
}
