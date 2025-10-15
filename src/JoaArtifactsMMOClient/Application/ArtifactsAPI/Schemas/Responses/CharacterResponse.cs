namespace Application.ArtifactsApi.Schemas.Responses;

public record CharacterResponse
{
    public required CharacterSchema Data { get; set; }
}
