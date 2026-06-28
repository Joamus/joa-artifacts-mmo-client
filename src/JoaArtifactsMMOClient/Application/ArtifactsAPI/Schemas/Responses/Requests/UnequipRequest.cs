namespace Application.ArtifactsApi.Schemas.Requests;

public record UnequipRequest
{
    // Camel-cased item slot
    public required string Slot { get; init; }
    public required int Quantity { get; init; }
}
