using System.Text.Json.Serialization;
using Application.ArtifactsApi.Schemas;

namespace Application.ArtifactsApi.Schemas.Responses;

public record FightResponse
{
    public FightResponseData Data { get; set; }
}

public record FightResponseData : GenericCharacterSchema
{
    public FightSchema Fight { get; set; }
}
