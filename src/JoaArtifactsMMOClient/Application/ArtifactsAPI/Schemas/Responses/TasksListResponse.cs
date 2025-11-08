using Application.Artifacts.Schemas;
using Application.Dtos;

namespace Application.ArtifactsApi.Schemas.Responses;

public record TasksListsResponse
{
    public required List<TasksFullSchema> Data { get; set; } = [];
}

public record TasksFullSchema
{
    public required string Code { get; set; } = "";
    public required int Level { get; set; }
    public required TaskType Type { get; set; }
    public required int MinQuantity { get; set; }
    public required int MaxQuantity { get; set; }
    public required Skill? Skill { get; set; }
    // Also rewards
}
