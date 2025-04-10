using System.Text.Json.Serialization;

namespace Application.ArtifactsApi.Schemas;

public record BlockedHitsSchema
{
    public int Fire { get; set; }

    public int Earth { get; set; }

    public int Water { get; set; }

    public int Air { get; set; }

    public int Total { get; set; }
}
