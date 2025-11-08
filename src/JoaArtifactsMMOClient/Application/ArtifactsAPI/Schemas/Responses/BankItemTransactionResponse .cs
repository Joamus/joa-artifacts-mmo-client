using System.Text.Json.Serialization;

namespace Application.ArtifactsApi.Schemas.Responses;

public record BankItemTransactionResponse
{
    public required BankItemTransactionData data { get; set; }
}

public record BankItemTransactionData : GenericCharacterSchema
{
    public List<DropSchema> Bank { get; set; } = [];
}
