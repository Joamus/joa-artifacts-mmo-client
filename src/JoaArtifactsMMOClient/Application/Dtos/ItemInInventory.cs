using Application.ArtifactsApi.Schemas;

namespace Application.Records;

public record ItemInInventory
{
    public required ItemSchema Item { get; set; }
    public int Quantity { get; set; }
}
