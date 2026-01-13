namespace Application.ArtifactsApi.Schemas.Responses;

public record BankGoldTransactionResponse
{
    public required BankGoldTransactionData Data { get; set; }
}

public record BankGoldTransactionData : GenericCharacterSchema
{
    public required BankGoldTransactionDto Bank { get; set; }
}
