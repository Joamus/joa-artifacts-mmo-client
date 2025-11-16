namespace Application.ArtifactsApi.Schemas;

public record EventSchema
{
    public required string Name { get; set; }
    public required string Code { get; set; }
    public required MapContentSchema Content { get; set; }

    public List<EventMapSchema> Maps { get; set; } = [];

    // Minutes duration
    public required int Duration { get; set; }

    // 1/rate every minute
    public required int Rate { get; set; }
}

public record EventMapSchema
{
    public required int MapId { get; set; }
    public required int X { get; set; }
    public required int Y { get; set; }
    public required MapLayer Layer { get; set; }
}
