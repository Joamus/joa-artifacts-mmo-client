using System.Text.Json.Serialization;

namespace Application.ArtifactsApi.Schemas;

public record FightEntity
{
    public required string Name { get; set; }
    public int Level { get; set; }

    public int Hp { get; set; }

    public int MaxHp { get; set; }

    public int AttackFire { get; set; }

    public int AttackEarth { get; set; }

    public int AttackWater { get; set; }

    public int AttackAir { get; set; }

    public int ResFire { get; set; }

    public int ResEarth { get; set; }

    public int ResWater { get; set; }

    public int ResAir { get; set; }
    public int Dmg { get; set; }

    public int DmgFire { get; set; }

    public int DmgEarth { get; set; }

    public int DmgWater { get; set; }

    public int DmgAir { get; set; }

    public int CriticalStrike { get; set; }
    public int Initiative { get; set; }
}
