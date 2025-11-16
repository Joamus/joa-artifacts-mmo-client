namespace Application.ArtifactsApi.Schemas;

public record ActiveEventSchema
{
    public required string Name { get; set; }
    public required string Code { get; set; }

    public required MapSchema Map { get; set; }

    public DateTime Expiration { get; set; }
    public DateTime CreatedAt { get; set; }

    // Minutes duration
    public required int Duration { get; set; }
}
