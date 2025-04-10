using System.Text.Json.Serialization;
using Application.ArtifactsApi.Schemas;
using Application.ArtifactsApi.Schemas;

namespace Application.ArtifactsApi.Schemas.Responses;

public record MoveResponse
{
    public MoveResponseData Data { get; set; }
}

public record MoveResponseData : GenericCharacterSchema
{
    public MapSchema Destination { get; set; }
}
