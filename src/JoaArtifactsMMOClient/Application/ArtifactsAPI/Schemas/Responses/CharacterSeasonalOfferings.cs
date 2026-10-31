using Application.Artifacts.Schemas;

namespace Application.ArtifactsApi.Schemas.Responses;

public record CharacterSeasonalOfferingsSchema
{
    public required DateTime Date { get; set; }
    public required DateTime ExpiresAt { get; set; }
    public required List<Skill> Skills { get; set; }
    public required int XpRewardPercent { get; set; } = 25;
    public required List<SeasonalOfferingSchema> Offerings { get; set; }
    public required List<SelectedOfferingSchema> SelectedOfferings { get; set; }
    public required List<SeasonalOfferingCategory> CompletedCategories { get; set; }
    public required List<OfferingFavorSchema> ActiveFavors { get; set; }
}
