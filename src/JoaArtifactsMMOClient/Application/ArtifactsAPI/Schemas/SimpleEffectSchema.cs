using System.Text.Json.Serialization;

namespace Application.ArtifactsApi.Schemas;

public record SimpleEffectSchema
{
    public string Code { get; set; }

    public int Value { get; set; }
}
