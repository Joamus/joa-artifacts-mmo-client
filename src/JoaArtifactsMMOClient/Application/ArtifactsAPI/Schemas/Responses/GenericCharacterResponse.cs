using System.Text.Json.Serialization;
using Application.ArtifactsApi.Schemas;

namespace Application.ArtifactsApi.Schemas.Responses;

// Response that contains cooldown and
public record GenericCharacterResponse
{
    public GenericCharacterSchema Data { get; set; }

}
