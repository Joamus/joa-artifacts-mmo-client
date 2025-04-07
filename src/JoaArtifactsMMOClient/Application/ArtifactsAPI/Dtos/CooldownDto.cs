using System.Text.Json.Serialization;

public record Cooldown
{
    [JsonPropertyName("total_seconds")]
    int TotalSeconds;

    [JsonPropertyName("remaining_seconds")]
    int RemainingSeconds;

    [JsonPropertyName("started_at")]
    DateTime StartedAt;

    [JsonPropertyName("expiration")]
    DateTime Expiration;

    [JsonPropertyName("reason")]
    // Allowed values are "movement"
    string Reason;
}
