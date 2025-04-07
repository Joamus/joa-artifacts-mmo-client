using System.Runtime.Serialization;
using System.Security.Permissions;
using System.Text.Json.Serialization;
using Application.ArtifactsAPI.Dtos;
using Newtonsoft.Json.Converters;

namespace Applcation.ArtifactsAPI.Dtos;

public record EffectDto
{
    [JsonPropertyName("code")]
    string Code;

    [JsonPropertyName("value")]
    int Value;
}
