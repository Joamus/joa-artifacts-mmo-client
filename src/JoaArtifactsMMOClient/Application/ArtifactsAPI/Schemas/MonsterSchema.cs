using System.Text.Json.Serialization;

namespace Application.ArtifactsApi.Schemas;

public record MonsterSchema
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("code")]
    public string Code { get; set; } = "";

    [JsonPropertyName("level")]
    public int Level { get; set; }

    [JsonPropertyName("hp")]
    public int Hp { get; set; }

    [JsonPropertyName("attack_fire")]
    public int AttackFire { get; set; }

    [JsonPropertyName("attack_earth")]
    public int AttackEarth { get; set; }

    [JsonPropertyName("attack_water")]
    public int AttackWater { get; set; }

    [JsonPropertyName("attack_air")]
    public int AttackAir { get; set; }

    [JsonPropertyName("res_fire")]
    public int ResFire { get; set; }

    [JsonPropertyName("res_earth")]
    public int ResEarth { get; set; }

    [JsonPropertyName("res_water")]
    public int ResWater { get; set; }

    [JsonPropertyName("res_air")]
    public int ResAir { get; set; }

    [JsonPropertyName("critical_strike")]
    public int CriticalStrike { get; set; }

    [JsonPropertyName("effects")]
    public List<SimpleEffectSchema> effects { get; set; } = [];

    [JsonPropertyName("min_gold")]
    public int MinGold { get; set; }

    [JsonPropertyName("max_gold")]
    public int MaxGold { get; set; }

    [JsonPropertyName("drops")]
    public List<DropRateSchema> Drops { get; set; } = [];
}
