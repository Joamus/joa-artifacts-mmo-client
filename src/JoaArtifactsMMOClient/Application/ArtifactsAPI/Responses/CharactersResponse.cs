using System.Text.Json.Serialization;
using Application.Character;

namespace Applcation.ArtifactsAPI.Responses;

public record CharactersResponse
{
    [JsonPropertyName("data")]
    List<CharacterDto> Data;
}
