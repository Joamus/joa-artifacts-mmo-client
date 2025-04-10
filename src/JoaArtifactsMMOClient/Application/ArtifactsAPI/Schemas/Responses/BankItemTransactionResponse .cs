using System.Text.Json.Serialization;
using Application.ArtifactsApi.Schemas;

namespace Application.ArtifactsApi.Schemas.Responses;

public record BankItemTransactionResponse
{
    public required BankItemTransactionData data { get; set;}
}

public record BankItemTransactionData : GenericCharacterSchema
{
    [JsonPropertyName("bank")]
    public List<DropSchema> Bank { get; set; }
}
