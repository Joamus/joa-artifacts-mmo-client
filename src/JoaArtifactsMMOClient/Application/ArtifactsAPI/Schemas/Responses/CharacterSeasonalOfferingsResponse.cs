namespace Application.ArtifactsApi.Schemas.Responses;

public record CharacterSeasonalOfferingsResponse
{
    public required CharacterSeasonalOfferingsSchema Data { get; set; }
}
