using System.Text.Json.Serialization;

namespace Application.ArtifactsApi.Schemas;

public record SimpleEffectSchema
{
    [JsonPropertyName("code")]
    public string Code { get; set; }

    [JsonPropertyName("value")]
    public int Value { get; set; }
}
