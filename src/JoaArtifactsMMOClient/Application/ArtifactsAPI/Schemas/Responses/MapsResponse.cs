namespace Application.ArtifactsApi.Schemas.Responses;

public record MapsResponse : PaginatedResult
{
    public List<MapSchema> Data { get; set; } = [];
}
