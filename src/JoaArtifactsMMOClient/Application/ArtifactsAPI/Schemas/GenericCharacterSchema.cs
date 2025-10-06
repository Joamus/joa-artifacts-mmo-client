using System.Text.Json.Serialization;
using Application.ArtifactsApi.Schemas;

namespace Application.ArtifactsApi.Schemas;

// Response that contains cooldown and
public record GenericCharacterSchema
{
    public required CooldownSchema Cooldown { get; set; }

    public required CharacterSchema Character { get; set; }
}
