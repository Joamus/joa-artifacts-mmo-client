using System.Text.Json.Serialization;
using Application.Character;

public record GetCharactersResponse
{
    [JsonPropertyName("data")]
    List<CharacterDto> data;
}
