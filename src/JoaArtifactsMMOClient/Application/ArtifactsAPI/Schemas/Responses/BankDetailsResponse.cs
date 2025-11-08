namespace Application.ArtifactsApi.Schemas.Responses;

public record BankDetailsResponse
{
    public required BankDetails Data { get; set; }
}

public record BankDetails
{
    public int Slots { get; set; }
    public int Expansions { get; set; }
    public int NextExpansionCost { get; set; }
    public int Gold { get; set; }
}
