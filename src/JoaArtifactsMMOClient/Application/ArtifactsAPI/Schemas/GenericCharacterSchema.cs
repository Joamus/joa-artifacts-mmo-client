using System.Text.Json.Serialization;
using Application.ArtifactsApi.Schemas;

namespace Application.ArtifactsApi.Schemas;

// Response that contains cooldown and
public record GenericCharacterSchema
{
    public CooldownSchema Cooldown { get; set; }

    public CharacterSchema Character { get; set; }
}
