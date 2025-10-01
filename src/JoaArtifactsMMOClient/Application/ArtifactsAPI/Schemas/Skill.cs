using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using Newtonsoft.Json.Converters;

namespace Application.Artifacts.Schemas;

public enum Skill
{
    Weaponcrafting,

    Gearcrafting,

    Jewelrycrafting,

    Cooking,
    Woodcutting,

    Mining,

    Alchemy,

    Fishing,
}
