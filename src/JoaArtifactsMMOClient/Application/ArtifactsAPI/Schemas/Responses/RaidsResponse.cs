namespace Application.ArtifactsApi.Schemas.Responses;

public record RaidsResponse : PaginatedResult
{
    public required List<RaidSchema> Data { get; set; }
}
