using System.Text.Json.Serialization;
using Application.ArtifactsApi.Schemas;

namespace Application.ArtifactsApi.Schemas.Responses;

public record MapsResponse : PaginatedResult {
	[JsonPropertyName("data")]
	public List<MapSchema> Data { get; set; } = [];
}
