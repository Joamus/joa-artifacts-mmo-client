using System.Text.Json.Serialization;

namespace Application.ArtifactsApi.Schemas.Responses;

public record NpcResponse : PaginatedResult
{
    public List<NpcSchema> Data { get; set; } = [];
}
