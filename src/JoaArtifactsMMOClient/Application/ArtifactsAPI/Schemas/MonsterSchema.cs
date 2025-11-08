namespace Application.ArtifactsApi.Schemas;

public record MonsterSchema : FightEntity
{
    public string Name { get; set; } = "";
    public MonsterType Type { get; set; }

    public string Code { get; set; } = "";

    public List<SimpleEffectSchema> Effects { get; set; } = [];

    public int MinGold { get; set; }

    public int MaxGold { get; set; }

    public List<DropRateSchema> Drops { get; set; } = [];
}

public enum MonsterType
{
    Normal,
    Elite,
    Boss,
}
