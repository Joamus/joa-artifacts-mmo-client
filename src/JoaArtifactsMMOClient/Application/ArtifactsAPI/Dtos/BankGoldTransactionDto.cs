using System.Text.Json.Serialization;

namespace Applcation.ArtifactsAPI.Dtos;

public record BankGoldTransactionDto
{
    [JsonPropertyName("quantity")]
    int Quantity;
}
