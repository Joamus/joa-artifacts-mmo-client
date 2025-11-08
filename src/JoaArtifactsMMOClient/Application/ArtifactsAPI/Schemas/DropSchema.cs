namespace Application.ArtifactsApi.Schemas;

public record DropSchema
{
    public string Code { get; set; } = "";

    public int Quantity { get; set; }
}
