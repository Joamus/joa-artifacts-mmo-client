using System.Text.Json.Serialization;

namespace Application.ArtifactsApi.Schemas.Responses;

public record NpcResponse : PaginatedResult {
	[JsonPropertyName("data")]
	public List<NpcSchema> Data { get; set; } = [];

}
