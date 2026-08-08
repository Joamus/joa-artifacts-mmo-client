namespace Application.ArtifactsApi.Schemas.Responses;

public record BankDetailsResponse
{
    public required BankDetails Data { get; set; }
}

public record BankDetails
{
    public required int Slots { get; set; }
    public required int Expansions { get; set; }
    public required int NextExpansionCost { get; set; }
    public required int Gold { get; set; }
}
