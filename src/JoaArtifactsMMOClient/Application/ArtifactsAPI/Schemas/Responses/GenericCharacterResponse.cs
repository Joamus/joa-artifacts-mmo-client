namespace Application.ArtifactsApi.Schemas.Responses;

// Response that contains cooldown and
public record GenericCharacterResponse
{
    public required GenericCharacterSchema Data { get; set; }
}
