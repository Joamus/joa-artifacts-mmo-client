using System.Text.Json.Serialization;
using Applcation.ArtifactsAPI.Dtos;
using Microsoft.VisualBasic;

namespace Application.ArtifactsAPI.Responses;

public record GatherResponse
{
    public required GatherResponseData data;
}

public record GatherResponseData : GenericCharacterResponse
{
    [JsonPropertyName("details")]
    GatherDto Details;
}
