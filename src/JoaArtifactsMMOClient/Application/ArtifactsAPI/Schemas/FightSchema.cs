using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using Application.ArtifactsApi.Schemas;
using Newtonsoft.Json.Converters;

namespace Application.ArtifactsApi.Schemas;

public record FightSchema
{
    public int Xp { get; set; }

    public int Gold { get; set; }

    public List<DropSchema> Drops { get; set; } = [];

    public int Turns { get; set; }

    public BlockedHitsSchema MonsterBlockedHits { get; set; }

    public BlockedHitsSchema PlayerBlockedHits { get; set; }

    public FightResult result { get; set; }
}

public enum FightResult
{
    Win,

    Loss,
}
