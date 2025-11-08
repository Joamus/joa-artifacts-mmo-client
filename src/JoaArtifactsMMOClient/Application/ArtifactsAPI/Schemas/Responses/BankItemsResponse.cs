namespace Application.ArtifactsApi.Schemas.Responses;

public record BankItemsResponse : PaginatedResult
{
    public required List<DropSchema> Data { get; set; } = [];
}
