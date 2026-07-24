using System.Text.Json.Serialization;

namespace Application.ArtifactsApi.Schemas;

public record DropRateSchema
{
    public string Code { get; set; } = "";

    public int Rate { get; set; }

    public int MinQuantity { get; set; }

    public int MaxQuantity { get; set; }
}
