namespace Application.ArtifactsApi.Schemas.Responses;

public record GetAllEventsResponse : PaginatedResult
{
    public List<EventSchema> Data { get; set; } = [];
}
