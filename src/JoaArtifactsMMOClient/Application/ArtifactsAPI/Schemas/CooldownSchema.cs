namespace Application.ArtifactsApi.Schemas;

public record CooldownSchema
{
    public DateTime Expiration { get; set; }

    // Allowed values are "movement"
    public string Reason { get; set; } = "";
}
