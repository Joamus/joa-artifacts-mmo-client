namespace Application.ArtifactsApi.Schemas.Responses;

public record FightResponse
{
    public required FightResponseData Data { get; set; }
}

public record FightResponseData
{
    public required CooldownSchema Cooldown { get; set; }
    public required FightSchema Fight { get; set; }
    public List<CharacterSchema> Characters { get; set; } = [];
}
