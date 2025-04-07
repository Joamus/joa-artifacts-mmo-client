using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using Application.ArtifactsAPI.Dtos;
using Newtonsoft.Json.Converters;

namespace Applcation.ArtifactsAPI.Dtos;

public record FightDto
{
    [JsonPropertyName("xp")]
    int Xp;

    [JsonPropertyName("gold")]
    int Gold;

    [JsonPropertyName("drops")]
    List<DropDto> Drops = [];

    [JsonPropertyName("turns")]
    int Turns;

    [JsonPropertyName("monster_blocked_hits")]
    List<BlockedHitsDto> MonsterBlockedHits = [];

    [JsonPropertyName("player_blocked_hits")]
    List<BlockedHitsDto> PlayerBlockedHits = [];

    [JsonPropertyName("result")]
    FightResult result;
}

[JsonConverter(typeof(StringEnumConverter))]
public enum FightResult
{
    [EnumMember(Value = "win")]
    Win,

    [EnumMember(Value = "loss")]
    Loss,
}
