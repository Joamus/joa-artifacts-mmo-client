using System.Text.Json.Serialization;

namespace Application.ArtifactsApi.Schemas.Responses;

public record ItemsResponse : PaginatedResult
{
    [JsonPropertyName("data")]
    public List<ItemSchema> Data { get; set; } = [];
}
