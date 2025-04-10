using System.Text.Json.Serialization;

namespace Application.ArtifactsApi.Schemas;

public record CooldownSchema
{
    public int TotalSeconds { get; set; }

    public int RemainingSeconds { get; set; }

    public DateTime StartedAt { get; set; }

    public DateTime Expiration { get; set; }

    // Allowed values are "movement"
    public string Reason { get; set; }
}
