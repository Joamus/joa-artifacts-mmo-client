using System.Text.Json.Serialization;
using Applcation.ArtifactsAPI.Dtos;
using Microsoft.VisualBasic;

namespace Application.ArtifactsAPI.Responses;

public record BankItemTransactionResponse
{
    public required BankItemTransactionData data;
}

public record BankItemTransactionData : GenericCharacterResponse
{
    [JsonPropertyName("bank")]
    List<DropDto> Bank;
}
