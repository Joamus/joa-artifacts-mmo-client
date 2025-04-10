using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using Newtonsoft.Json.Converters;

namespace Application.Artifacts.Schemas;

public enum Skill
{
    //[EnumMember(Value = "weaponcrafting")]
    //[JsonStringEnumMemberName("weaponcrafting")]
    Weaponcrafting,
 
    //[EnumMember(Value = "gearcrafting")]
    //[JsonStringEnumMemberName("gearcrafting")]
    Gearcrafting,

    //[EnumMember(Value = "jewelrycrafting")]
    //[JsonStringEnumMemberName("jewelrycrafting")]
    Jewelrycrafting,

    //[EnumMember(Value = "cooking")]
    //[JsonStringEnumMemberName("cooking")]
    Cooking,

    //[EnumMember(Value = "woodcutting")]
    //[JsonStringEnumMemberName("woodcutting")]
    Woodcutting,

    //[EnumMember(Value = "mining")]
    //[JsonStringEnumMemberName("mining")]
    Mining,

    //[EnumMember(Value = "alchemy")]
    //[JsonStringEnumMemberName("alchemy")]
    Alchemy,

    //[EnumMember(Value = "fishing")]
    //[JsonStringEnumMemberName("fishing")]
    Fishing,
}
