namespace Application.Character;

public static class PlayerSkill
{
    public static List<string> AllSkills { get; } =
        [
            Weaponcrafting,
            Gearcrafting,
            Jewelrycrafting,
            Mining,
            Alchemy,
            Woodcutting,
            Fishing,
            Cooking,
        ];

    public const string Mining = "mining";

    public const string Woodcutting = "woodcutting";

    public const string Fishing = "fishing";

    public const string Weaponcrafting = "weapon_crafting";

    public const string Gearcrafting = "gear_crafting";

    public const string Alchemy = "alchemy";

    public const string Jewelrycrafting = "jewelry_crafting";
    public const string Cooking = "cooking";
}
