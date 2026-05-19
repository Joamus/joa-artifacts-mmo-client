namespace Application.ArtifactsApi.Schemas.Responses;

public record MoveResponse : PaginatedResult
{
    public MoveResponseData Data { get; set; }
}

public record MoveResponseData : GenericCharacterSchema
{
    public MapSchema Destination { get; set; }
}
