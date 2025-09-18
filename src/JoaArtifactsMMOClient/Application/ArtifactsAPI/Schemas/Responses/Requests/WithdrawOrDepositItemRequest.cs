using System.Text.Json.Serialization;

namespace Application.ArtifactsApi.Schemas.Requests;

public record WithdrawOrDepositItemRequest
{
    [JsonPropertyName("code")]
    public required string Code { get; set; }

    [JsonPropertyName("quantity")]
    public required int Quantity { get; set; }
}
