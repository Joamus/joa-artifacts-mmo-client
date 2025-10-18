using Application.ArtifactsApi.Schemas;
using Microsoft.OpenApi.Extensions;

public static class EffectService
{
    public static readonly List<string> preFightEffects =
    [
        Effect.BoostHp,
        Effect.BoostDmgAir,
        Effect.BoostDmgEarth,
        Effect.BoostDmgFire,
        Effect.BoostDmgWater,
        Effect.BoostResAir,
        Effect.BoostResEarth,
        Effect.BoostResFire,
        Effect.BoostResWater,
    ];

    public static void ApplyEffect(FightEntity entity, SimpleEffectSchema effect)
    {
        if (effect.Code == Effect.BoostHp)
        {
            entity.Hp += effect.Value;

            // For now this is should work, monsters don't have Max Hp
            var entityAsPlayer = (CharacterSchema)entity;

            entityAsPlayer.MaxHp += effect.Value;
        }
        else if (effect.Code == Effect.BoostHp)
        {
            ApplyBoostDmg(entity, effect);
        }
        else if (effect.Code == Effect.BoostDmgAir)
        {
            ApplyBoostDmg(entity, effect);
        }
        else if (effect.Code == Effect.BoostDmgEarth)
        {
            ApplyBoostDmg(entity, effect);
        }
        else if (effect.Code == Effect.BoostDmgFire)
        {
            ApplyBoostDmg(entity, effect);
        }
        else if (effect.Code == Effect.BoostDmgWater)
        {
            ApplyBoostDmg(entity, effect);
        }
        else if (effect.Code == Effect.BoostResAir)
        {
            ApplyResDmg(entity, effect);
        }
        else if (effect.Code == Effect.BoostResEarth)
        {
            ApplyResDmg(entity, effect);
        }
        else if (effect.Code == Effect.BoostResFire)
        {
            ApplyResDmg(entity, effect);
        }
        else if (effect.Code == Effect.BoostResWater)
        {
            ApplyResDmg(entity, effect);
        }
    }

    public static void ApplyBoostDmg(FightEntity entity, SimpleEffectSchema effect)
    {
        if (effect.Code == Effect.BoostDmgAir)
        {
            entity.DmgAir += effect.Value;
        }
        else if (effect.Code == Effect.BoostDmgEarth)
        {
            entity.DmgEarth += effect.Value;
        }
        else if (effect.Code == Effect.BoostDmgFire)
        {
            entity.DmgFire += effect.Value;
        }
        else if (effect.Code == Effect.BoostDmgWater)
        {
            entity.DmgWater += effect.Value;
        }
    }

    public static void ApplyResDmg(FightEntity entity, SimpleEffectSchema effect)
    {
        if (effect.Code == Effect.BoostResAir)
        {
            entity.ResAir += effect.Value;
        }
        else if (effect.Code == Effect.BoostResEarth)
        {
            entity.ResEarth += effect.Value;
        }
        else if (effect.Code == Effect.BoostResFire)
        {
            entity.ResFire += effect.Value;
        }
        else if (effect.Code == Effect.BoostResWater)
        {
            entity.ResWater += effect.Value;
        }
    }
}

public static class Effect
{
    public static readonly string BoostHp = "boost_hp";
    public static readonly string BoostDmgFire = "boost_dmg_fire";
    public static readonly string BoostDmgEarth = "boost_dmg_earth";
    public static readonly string BoostDmgAir = "boost_dmg_air";
    public static readonly string BoostDmgWater = "boost_dmg_water";
    public static readonly string Restore = "restore";
    public static readonly string Healing = "healing";
    public static readonly string Antipoison = "antipoison";
    public static readonly string Poison = "poison";
    public static readonly string Lifesteal = "lifesteal";
    public static readonly string Reconstitution = "reconstitution";
    public static readonly string Burn = "burn";
    public static readonly string BoostResFire = "boost_res_fire";
    public static readonly string BoostResEarth = "boost_res_earth";
    public static readonly string BoostResAir = "boost_res_air";
    public static readonly string BoostResWater = "boost_res_water";
    public static readonly string Corrupted = "corrupted";
    public static readonly string Guard = "guard";
    public static readonly string Shell = "shell";
    public static readonly string Frenzy = "frenzy";
    public static readonly string VoidDrain = "void_drain";
    public static readonly string BerserkerRage = "berserker_rage";
    public static readonly string VampiricStrike = "vampiric_strike";
    public static readonly string HealingAura = "healing_aura";
    public static readonly string Barrier = "barrier";
    public static readonly string SplashRestore = "splash_restore";
    public static readonly string Heal = "heal";
    public static readonly string Gold = "gold";
    public static readonly string Teleport = "teleport";
    public static readonly string FireAttack = "attack_fire";
    public static readonly string WaterAttack = "attack_water";
    public static readonly string AirAttack = "attack_air";
    public static readonly string EarthAttack = "attack_earth";
    public static readonly string Damage = "dmg";
    public static readonly string FireDamage = "dmg_fire";
    public static readonly string WaterDamage = "dmg_water";
    public static readonly string AirDamage = "dmg_air";
    public static readonly string EarthDamage = "dmg_earth";
    public static readonly string FireResistance = "res_fire";
    public static readonly string WaterResistance = "res_water";
    public static readonly string AirResistance = "res_air";
    public static readonly string EarthResistance = "res_earth";
    public static readonly string CriticalStrike = "critical_strike";
    public static readonly string Wisdom = "wisdom";
    public static readonly string Prospecting = "prospecting";
    public static readonly string Woodcutting = "woodcutting";
    public static readonly string Fishing = "fishing";
    public static readonly string Mining = "mining";
    public static readonly string Alchemy = "alchemy";
    public static readonly string Hitpoints = "hp";
    public static readonly string InventorySpace = "inventory_space";
    public static readonly string Haste = "haste";
    public static readonly string Initiative = "initiative";
    public static readonly string Threat = "threat";
}
