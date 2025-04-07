using System.Text.Json.Serialization;

namespace Applcation.ArtifactsAPI.Dtos;

public record DropDto
{
    [JsonPropertyName("code")]
    string Code = "";

    [JsonPropertyName("quantity")]
    int Quantity;
}
