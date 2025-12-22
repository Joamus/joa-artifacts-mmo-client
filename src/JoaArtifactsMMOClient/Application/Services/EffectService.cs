using Application.ArtifactsApi.Schemas;
using Application.Jobs;
using Applicaton.Services.FightSimulator;

public static class EffectService
{
    public const double PERCENTAGE_PRE_EFFECT_SHOULD_INFLUENCE = 0.85;
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
        switch (effect.Code)
        {
            case Effect.BoostHp:
                entity.Hp += effect.Value;

                // For now this is should work, monsters don't have Max Hp
                var entityAsPlayer = (CharacterSchema)entity;

                entityAsPlayer.MaxHp += effect.Value;
                break;
            case Effect.BoostDmgAir:
            case Effect.BoostDmgEarth:
            case Effect.BoostDmgFire:
            case Effect.BoostDmgWater:
                ApplyBoostDmg(entity, effect);
                break;
            case Effect.BoostResAir:
            case Effect.BoostResEarth:
            case Effect.BoostResFire:
            case Effect.BoostResWater:
                ApplyResDmg(entity, effect);
                break;
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

    /** This method is for prefight effects, because we'll always use them if we have them equipped */
    public static List<string> GetPotionEffectsToSkip(
        CharacterSchema characterSchema,
        MonsterSchema monsterSchema
    )
    {
        List<string> effectsToSkip = [];

        // Boost
        if (characterSchema.AttackAir == 0)
        {
            effectsToSkip.Add(Effect.BoostDmgAir);
        }
        if (characterSchema.AttackEarth == 0)
        {
            effectsToSkip.Add(Effect.BoostDmgEarth);
        }
        if (characterSchema.AttackFire == 0)
        {
            effectsToSkip.Add(Effect.BoostDmgFire);
        }
        if (characterSchema.AttackWater == 0)
        {
            effectsToSkip.Add(Effect.BoostDmgWater);
        }

        // Res
        if (monsterSchema.AttackAir == 0)
        {
            effectsToSkip.Add(Effect.BoostResAir);
        }
        if (monsterSchema.AttackEarth == 0)
        {
            effectsToSkip.Add(Effect.BoostResEarth);
        }
        if (monsterSchema.AttackFire == 0)
        {
            effectsToSkip.Add(Effect.BoostResFire);
        }
        if (monsterSchema.AttackWater == 0)
        {
            effectsToSkip.Add(Effect.BoostResWater);
        }

        if (!monsterSchema.Effects.Exists(effect => effect.Code == Effect.Poison))
        {
            effectsToSkip.Add(Effect.Antipoison);
        }

        return effectsToSkip;
    }

    public static bool IsPreFightPotion(ItemSchema item)
    {
        return item.Effects.Exists(effect => preFightEffects.Contains(effect.Code));
    }

    public static bool SimpleIsPreFightPotionWorthUsing(FightSimResult fightSimWithoutPotions)
    {
        // Pretty rough heuristic, but it will help to avoid gathering dmg boost potions to fight low level monsters
        // Update: Our reasoning now is just that we only use potions, if it helps us win a fight
        return !fightSimWithoutPotions.Outcome.ShouldFight;
        // && fightSimWithoutPotions.Outcome.TotalTurns
        //     > ObtainSuitablePotions.AMOUNT_OF_TURNS_TO_NOT_USE_PREFIGHT_POTS;
    }

    public static bool IsPotionWorthUsing(
        ItemSchema item,
        FightOutcome noPotion,
        FightOutcome withPotion
    )
    {
        // Potions don't have multiple effects at the time of writing this, but we basically only want to use
        // this method for "pre fight potions", e.g. boost potions, because we might end up wasting a lot of them.
        // If the potion makes us win the fight, then we should consider using it
        if (!noPotion.ShouldFight && withPotion.ShouldFight)
        {
            return true;
        }

        // Even if we are losing, we should consider using the potion if the outcome is better
        if (
            noPotion.Result == FightResult.Loss
            && (
                withPotion.Result == FightResult.Win
                || withPotion.ShouldFight
                || withPotion.PlayerHp > noPotion.PlayerHp
                || withPotion.MonsterHp < noPotion.MonsterHp
            )
        )
        {
            return true;
        }
        // For now, we only want to use these potions if it changes whether we should fight or not, or whether we win or not
        return (!noPotion.ShouldFight && withPotion.ShouldFight)
            || (noPotion.Result == FightResult.Loss && withPotion.Result == FightResult.Win);

        // Now we have to figure out the cost/benefit of using the potion. We don't want to use "boost" potions, if we don't really need to.
        // foreach (var effect in item.Effects)
        // {
        //     // if (effect.Code.StartsWith("boost_dmg") || effect.Code.StartsWith("boost_res"))
        //     // {
        //     // double differenceItShouldMake =
        //     //     1 - (effect.Value * 0.01 * PERCENTAGE_PRE_EFFECT_SHOULD_INFLUENCE);

        //     // // If the potion's effect is e.g. 12% more damage, then we should either be around 12% faster or have 12% more HP after the fight is over
        //     // if (
        //     //     noPotion.TotalTurns * differenceItShouldMake >= withPotion.TotalTurns
        //     //     || withPotion.PlayerHp * differenceItShouldMake >= noPotion.PlayerHp
        //     // )
        //     // {
        //     //     return true;
        //     // }
        //     // else
        //     // {
        //     //     return false;
        //     // }
        //     // }
        // }
    }
}

public static class Effect
{
    public const string BoostHp = "boost_hp";
    public const string BoostDmgFire = "boost_dmg_fire";
    public const string BoostDmgEarth = "boost_dmg_earth";
    public const string BoostDmgAir = "boost_dmg_air";
    public const string BoostDmgWater = "boost_dmg_water";
    public const string Restore = "restore";
    public const string Healing = "healing";
    public const string Antipoison = "antipoison";
    public const string Poison = "poison";
    public const string Lifesteal = "lifesteal";
    public const string Reconstitution = "reconstitution";
    public const string Burn = "burn";
    public const string BoostResFire = "boost_res_fire";
    public const string BoostResEarth = "boost_res_earth";
    public const string BoostResAir = "boost_res_air";
    public const string BoostResWater = "boost_res_water";
    public const string Corrupted = "corrupted";
    public const string Guard = "guard";
    public const string Shell = "shell";
    public const string Frenzy = "frenzy";
    public const string VoidDrain = "void_drain";
    public const string BerserkerRage = "berserker_rage";
    public const string VampiricStrike = "vampiric_strike";
    public const string HealingAura = "healing_aura";
    public const string Barrier = "barrier";
    public const string SplashRestore = "splash_restore";
    public const string Heal = "heal";
    public const string Gold = "gold";
    public const string Teleport = "teleport";
    public const string FireAttack = "attack_fire";
    public const string WaterAttack = "attack_water";
    public const string AirAttack = "attack_air";
    public const string EarthAttack = "attack_earth";
    public const string Damage = "dmg";
    public const string FireDamage = "dmg_fire";
    public const string WaterDamage = "dmg_water";
    public const string AirDamage = "dmg_air";
    public const string EarthDamage = "dmg_earth";
    public const string FireResistance = "res_fire";
    public const string WaterResistance = "res_water";
    public const string AirResistance = "res_air";
    public const string EarthResistance = "res_earth";
    public const string CriticalStrike = "critical_strike";
    public const string Wisdom = "wisdom";
    public const string Prospecting = "prospecting";
    public const string Woodcutting = "woodcutting";
    public const string Fishing = "fishing";
    public const string Mining = "mining";
    public const string Alchemy = "alchemy";
    public const string Hitpoints = "hp";
    public const string InventorySpace = "inventory_space";
    public const string Haste = "haste";
    public const string Initiative = "initiative";
    public const string Threat = "threat";
}
