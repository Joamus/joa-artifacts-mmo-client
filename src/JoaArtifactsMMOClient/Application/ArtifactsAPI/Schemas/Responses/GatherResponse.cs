using System.Text.Json.Serialization;
using Application.ArtifactsApi.Schemas;

namespace Application.ArtifactsApi.Schemas.Responses;

public record GatherResponse
{
    public SkillDataSchema Data { get; set; }
}

public record SkillDataSchema : GenericCharacterSchema
{
    public SkillInfoSchema Details { get; set; }
}
