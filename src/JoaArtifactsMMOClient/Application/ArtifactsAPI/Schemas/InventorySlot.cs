namespace Application.ArtifactsApi.Schemas;

public record InventorySlot
{
    public string Code { get; set; } = "";

    public int Quantity { get; set; }
}

public record EquipmentSlot
{
    public required string Slot { get; set; }

    public required string Code { get; set; }

    public required int Quantity { get; set; }
}
