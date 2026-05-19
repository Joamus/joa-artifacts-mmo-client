using Application.ArtifactsApi.Schemas.Responses;

namespace Application.ArtifactsApi.Schemas;

public record PendingItemsResponse : PaginatedResult
{
    public required List<PendingItemSchema> Data { get; set; }
}

public record PendingItemSchema
{
    public required string Id { get; set; }
    public string Description { get; set; } = "";
    public required List<SimpleItemSchema> Items { get; set; }
}
