using System.Text.Json.Serialization;
using Application.ArtifactsApi.Schemas;

namespace Application.ArtifactsApi.Schemas.Responses;

public record RecycleResponse
{
    public required RecycleSchema Data { get; set; }
}

public record RecycleSchema : GenericCharacterSchema
{
    public required RecyclingItemsSchema Details { get; set; }
}

public record RecyclingItemsSchema
{
    public List<DropSchema> Items { get; set; } = [];
}
