using System.Text.Json.Serialization;
using Application.ArtifactsApi.Schemas;

namespace Application.ArtifactsApi.Schemas.Responses;

public record BankGoldTransactionResponse
{
    public required BankGoldTransactionData Data { get; set; }
}

public record BankGoldTransactionData : GenericCharacterSchema
{
    [JsonPropertyName("bank")]
    public BankGoldTransactionDto Bank { get; set; }
}
