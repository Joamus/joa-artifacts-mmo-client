using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace Application.ArtifactsAPI.Dtos;

public record BlockedHitsDto
{
    [JsonPropertyName("fire")]
    int Fire;

    [JsonPropertyName("earth")]
    int Earth;

    [JsonPropertyName("water")]
    int Water;

    [JsonPropertyName("air")]
    int Air;

    [JsonPropertyName("total")]
    int Total;
}
