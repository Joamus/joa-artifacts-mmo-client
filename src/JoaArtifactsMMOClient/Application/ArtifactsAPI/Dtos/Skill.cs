using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using Newtonsoft.Json.Converters;

[JsonConverter(typeof(StringEnumConverter))]
public enum Skill
{
    [EnumMember(Value = "weaponcrafting")]
    Weaponcrafting,

    [EnumMember(Value = "gearcrafting")]
    Gearcrafting,

    [EnumMember(Value = "jewelrycrafting")]
    Jewelrycrafting,

    [EnumMember(Value = "cooking")]
    Cooking,

    [EnumMember(Value = "woodcutting")]
    Woodcutting,

    [EnumMember(Value = "mining")]
    Mining,

    [EnumMember(Value = "alchemy")]
    Alchemy,
}
