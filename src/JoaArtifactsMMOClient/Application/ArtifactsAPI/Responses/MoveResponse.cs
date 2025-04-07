using System.Text.Json.Serialization;
using Applcation.ArtifactsAPI.Dtos;
using Microsoft.VisualBasic;

namespace Application.ArtifactsAPI.Responses;

public record MoveResponse
{
    public required MoveResponseData data;
}

public record MoveResponseData : GenericCharacterResponse
{
    [JsonPropertyName("destination")]
    DestinationDto Destination;
}
