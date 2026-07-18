namespace Application.ArtifactsApi.Schemas;

public record CooldownSchema
{
    public required DateTime Expiration { get; set; }

    // Allowed values are "movement"
    public string Reason { get; set; } = "";
}
