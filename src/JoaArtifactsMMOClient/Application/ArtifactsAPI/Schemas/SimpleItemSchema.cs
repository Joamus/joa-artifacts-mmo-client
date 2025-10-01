using System.Text.Json.Serialization;

namespace Application.ArtifactsApi.Schemas;

public record SimpleItemSchema
{
    public string Code { get; set; } = "";

    public int Quantity { get; set; }
}
