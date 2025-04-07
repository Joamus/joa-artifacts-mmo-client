using System.Text.Json.Serialization;
using Applcation.ArtifactsAPI.Dtos;
using Microsoft.VisualBasic;

namespace Application.ArtifactsAPI.Responses;

public record BankGoldTransactionResponse
{
    public required BankGoldTransactionData data;
}

public record BankGoldTransactionData : GenericCharacterResponse
{
    [JsonPropertyName("bank")]
    BankGoldTransactionDto Bank;
}
