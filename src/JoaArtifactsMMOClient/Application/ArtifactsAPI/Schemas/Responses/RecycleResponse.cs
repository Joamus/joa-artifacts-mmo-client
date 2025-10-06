namespace Application.ArtifactsApi.Schemas.Responses;

public record RecycleResponse
{
    public required RecyclingDataSchema Data { get; set; }
}

public record RecyclingDataSchema : GenericCharacterSchema
{
    public required RecyclingItemsSchema Details { get; set; }
}

public record RecyclingItemsSchema
{
    public List<DropSchema> Items { get; set; } = [];
}
