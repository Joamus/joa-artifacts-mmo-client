using System.Text.Json.Serialization;

namespace Applcation.ArtifactsAPI.Dtos;

public record InventoryItemDto
{
    [JsonPropertyName("slot")]
    int Slot;

    [JsonPropertyName("code")]
    string Code = "";

    [JsonPropertyName("quantity")]
    int Quantity;
}
