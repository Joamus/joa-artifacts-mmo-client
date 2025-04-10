using System.Text.Json.Serialization;

namespace Application.ArtifactsApi.Schemas.Responses;

public record MonstersResponse {
	[JsonPropertyName("data")]
	public List<MonsterSchema> Data { get; set; } = [];
}
