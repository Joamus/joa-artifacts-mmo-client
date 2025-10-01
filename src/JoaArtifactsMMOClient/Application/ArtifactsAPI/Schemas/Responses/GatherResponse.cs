using System.Text.Json.Serialization;
using Application.ArtifactsApi.Schemas;

namespace Application.ArtifactsApi.Schemas.Responses;

public record GatherResponse
{
    public required SkillDataSchema Data { get; set; }
}

public record SkillDataSchema : GenericCharacterSchema
{
    public required SkillInfoSchema Details { get; set; }
}
