using System.Text.Json.Serialization;

namespace Application.ArtifactsApi.Schemas;

public record InventorySlot
{
    [JsonPropertyName("slot")]
    public string Slot { get; set; } = "";

    [JsonPropertyName("code")]
    public string Code { get; set; } = "";

    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }
}
