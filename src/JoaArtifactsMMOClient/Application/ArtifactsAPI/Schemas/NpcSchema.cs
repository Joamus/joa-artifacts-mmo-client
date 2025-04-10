using System.Text.Json.Serialization;

namespace Application.ArtifactsApi.Schemas;

public record NpcSchema {
	[JsonPropertyName("name")]
	public string Name { get; set; }
	
	[JsonPropertyName("code")]
	public string Code { get; set; }
	[JsonPropertyName("description")]
	public string Description { get; set; }

	// Allowed value is merchant;
	[JsonPropertyName("type")]
	public string Type { get; set; }
}
