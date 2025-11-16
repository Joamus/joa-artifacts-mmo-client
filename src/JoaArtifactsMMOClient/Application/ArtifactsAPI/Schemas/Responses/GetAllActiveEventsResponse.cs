namespace Application.ArtifactsApi.Schemas.Responses;

public record GetActiveEventsResponse : PaginatedResult
{
    public List<ActiveEventSchema> Data { get; set; } = [];
}
