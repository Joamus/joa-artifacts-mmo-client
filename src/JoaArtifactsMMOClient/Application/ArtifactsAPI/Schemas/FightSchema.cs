using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using Application.ArtifactsApi.Schemas;
using Newtonsoft.Json.Converters;

namespace Application.ArtifactsApi.Schemas;

public record FightSchema
{
    public required int Turns { get; set; }

    public required List<CharacterFightSchema> Characters { get; set; } = [];

    public required string Opponent { get; set; } = "";

    public required FightResult result { get; set; }
}

public enum FightResult
{
    Win,

    Loss,
}

public record CharacterFightSchema
{
    public required string CharacterName { get; set; } = "";
    public required int Xp { get; set; }
    public required int Gold { get; set; }
    public required List<DropSchema> Drops { get; set; } = [];
    public required int FinalHp { get; set; }
}
