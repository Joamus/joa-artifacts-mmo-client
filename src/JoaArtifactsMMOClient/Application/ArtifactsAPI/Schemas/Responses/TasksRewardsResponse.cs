namespace Application.ArtifactsApi.Schemas.Responses;

public record TasksRewardsResponse
{
    public required List<DropRateSchema> Data { get; set; } = [];
}
