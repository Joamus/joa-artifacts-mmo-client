namespace Application.ArtifactsApi.Schemas.Responses;

public record PaginatedResult
{
    public int Total { get; set; }
    public int Page { get; set; }
    public int Size { get; set; }
    public int Pages { get; set; }
}
