using System.Text.Json.Serialization;
using Applcation.ArtifactsAPI.Dtos;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.VisualBasic;

namespace Application.ArtifactsAPI.Responses;

public record FightResponse
{
    public required FightResponseData data;
}

public record FightResponseData : GenericCharacterResponse
{
    [JsonPropertyName("fight")]
    FightDto Fight;
}
