namespace Application.ArtifactsApi.Schemas.Responses;

public record ResourceResponse : PaginatedResult
{
    public List<ResourceSchema> Data { get; set; } = [];
}
