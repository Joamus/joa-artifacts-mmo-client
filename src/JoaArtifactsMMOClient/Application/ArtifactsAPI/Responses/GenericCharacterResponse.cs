using System.Text.Json.Serialization;

namespace Application.ArtifactsAPI.Responses;

// Response that contains cooldown and
public record GenericCharacterResponse
{
    [JsonPropertyName("cooldown")]
    Cooldown Cooldown;

    [JsonPropertyName("character")]
    CharacterDto Character;
}
