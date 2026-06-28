namespace Application.ArtifactsApi.Schemas.Requests;

public record EquipRequest
{
    public required string Code { get; init; }

    // Camel-cased item slot
    public required string Slot { get; init; }
    public required int Quantity { get; init; }
}
