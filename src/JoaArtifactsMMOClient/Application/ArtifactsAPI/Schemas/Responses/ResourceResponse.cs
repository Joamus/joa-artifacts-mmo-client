using System.Text.Json.Serialization;
using Application.Artifacts.Schemas;

namespace Application.ArtifactsApi.Schemas.Responses;

public record ResourceResponse : PaginatedResult {

	[JsonPropertyName("data")]
	public List<ResourceSchema> Data { get; set; } = [];

}
