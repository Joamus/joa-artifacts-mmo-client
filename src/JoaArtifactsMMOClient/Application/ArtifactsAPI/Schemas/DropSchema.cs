using System.Text.Json.Serialization;

namespace Application.ArtifactsApi.Schemas;

public record DropSchema
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = "";

    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }
}
