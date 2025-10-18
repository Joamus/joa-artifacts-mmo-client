using System.Text.Json.Serialization;

namespace Application.ArtifactsApi.Schemas;

public record MonsterSchema : FightEntity
{
    public string Name { get; set; } = "";

    public string Code { get; set; } = "";

    public List<SimpleEffectSchema> effects { get; set; } = [];

    public int MinGold { get; set; }

    public int MaxGold { get; set; }

    public List<DropRateSchema> Drops { get; set; } = [];
}
