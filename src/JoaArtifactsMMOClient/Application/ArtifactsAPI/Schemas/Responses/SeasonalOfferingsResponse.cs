namespace Application.ArtifactsApi.Schemas.Responses;

public record DailySeasonalOfferingsResponse
{
    public List<DailySeasonalOfferingSchema> Data { get; set; } = [];
}
