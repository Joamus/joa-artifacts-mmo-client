using System.Text.Json.Serialization;

namespace Applcation.ArtifactsAPI.Dtos;

public record MapDto
{
    [JsonPropertyName("x")]
    int X;

    [JsonPropertyName("y")]
    int Y;

    [JsonPropertyName("content")]
    ContentDto Content;
}
