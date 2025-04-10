using System.Text.Json.Serialization;

namespace Application.ArtifactsApi.Schemas.Responses;

public record CharactersResponse
{
    [JsonPropertyName("data")]
    public required List<CharacterSchema> Data { get; set; } = [];
}
