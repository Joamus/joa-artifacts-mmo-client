using System.Text.Json.Serialization;

namespace Application.ArtifactsApi.Schemas;

public record BankGoldTransactionDto
{
    public int Quantity { get; set; }
}
