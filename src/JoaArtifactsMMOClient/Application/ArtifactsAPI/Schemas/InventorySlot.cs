using System.Text.Json.Serialization;

namespace Application.ArtifactsApi.Schemas;

public record InventorySlot
{
    public int Slot { get; set; }

    public string Code { get; set; } = "";

    public int Quantity { get; set; }
}

public record EquipmentSlot
{
    public string Slot { get; set; } = "";

    public string Code { get; set; } = "";

    public int Quantity { get; set; }
}
