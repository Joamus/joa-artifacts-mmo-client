using Application;
using Application.ArtifactsApi.Schemas;
using Application.Character;
using Application.Records;
using Application.Services;
using Application.Services.Combat;
using OneOf.Types;

namespace Applicaton.Services.FightSimulator;

public class FightSimulator
{
    private static readonly double CRIT_DAMAGE_MODIFIER = 0.5;
    private static readonly int MAX_LEVEL = 50;
    private static readonly double PERCENTAGE_OF_SIMS_TO_WIN = 0.85;
    private static readonly double RESTORE_EFFECT_MAX_HP_THRESHOLD = 0.50;

    private static readonly int MAX_AMOUNT_OF_USED_POTIONS = 10;
    private static readonly int SHELL_EFFECT_DURATION = 3;
    private static readonly int VAMPIRIC_STRIKE_COOLDOWN_TURNS = 3;
    private static readonly int SHELL_ACTIVATION_THRESHOLD_HP_PERCENTAGE = 40;
    private static List<EquipmentTypeMapping> allEquipmentTypes { get; } =
        new List<EquipmentTypeMapping>
        {
            new EquipmentTypeMapping { ItemType = "weapon", Slot = "WeaponSlot" },
            new EquipmentTypeMapping { ItemType = "body_armor", Slot = "BodyArmorSlot" },
            new EquipmentTypeMapping { ItemType = "leg_armor", Slot = "LegArmorSlot" },
            new EquipmentTypeMapping { ItemType = "helmet", Slot = "HelmetSlot" },
            new EquipmentTypeMapping { ItemType = "boots", Slot = "BootsSlot" },
            new EquipmentTypeMapping { ItemType = "ring", Slot = "Ring1Slot" },
            new EquipmentTypeMapping { ItemType = "ring", Slot = "Ring2Slot" },
            new EquipmentTypeMapping { ItemType = "amulet", Slot = "AmuletSlot" },
            new EquipmentTypeMapping { ItemType = "shield", Slot = "ShieldSlot" },
            new EquipmentTypeMapping { ItemType = "utility", Slot = "Utility1Slot" },
            new EquipmentTypeMapping { ItemType = "utility", Slot = "Utility2Slot" },
            new EquipmentTypeMapping { ItemType = "rune", Slot = "RuneSlot" },
            new EquipmentTypeMapping { ItemType = "artifact", Slot = "Artifact1Slot" },
            new EquipmentTypeMapping { ItemType = "artifact", Slot = "Artifact2Slot" },
            new EquipmentTypeMapping { ItemType = "artifact", Slot = "Artifact3Slot" },
        };

    // We assume that monsters will crit more often than us, just to ensure that we don't take on fights too often, that we will probably not win.
    // private static readonly double MONSTER_CRIT_BIAS = 1.25;

    private static ILogger logger = AppLogger.GetLogger();

    public static FightOutcome CalculateFightOutcome(
        CharacterSchema originalSchema,
        MonsterSchema monster,
        GameState gameState,
        bool playerFullHp = true
    )
    {
        List<FightOutcome> outcomes = [];

        /**
          * Really hacky, but we want to calculate multiple outcomes - one with normal starting values, and others where we add onto the crit chance.
          * This is an attempt at making the crit calculations deterministic, so the outcome always is the same
        **/
        int iterations = 4;

        int addedCritChance = -25;

        CombatLog? firstCombatLog = null;

        for (int i = 0; i < iterations; i++)
        {
            addedCritChance += 25;
            List<FightSimUtility> potions = [];

            var monsterClone = monster with { };

            var initMonsterHp = monsterClone.Hp;
            CharacterSchema characterSchema = originalSchema with { };

            // Add runes to this
            List<SimpleEffectSchema> runeEffects = [];

            var matchingRuneItem = !string.IsNullOrWhiteSpace(characterSchema.RuneSlot)
                ? gameState.ItemsDict[characterSchema.RuneSlot]
                : null;

            if (matchingRuneItem is not null)
            {
                foreach (var effect in matchingRuneItem.Effects)
                {
                    runeEffects.Add(effect);
                }
            }

            if (characterSchema.Utility1SlotQuantity > 0)
            {
                potions.Add(
                    new FightSimUtility
                    {
                        Item = gameState.ItemsDict.GetValueOrNull(characterSchema.Utility1Slot)!,
                        OriginalQuantity = characterSchema.Utility1SlotQuantity,
                        Quantity = characterSchema.Utility1SlotQuantity,
                    }
                );
            }

            if (characterSchema.Utility2SlotQuantity > 0)
            {
                potions.Add(
                    new FightSimUtility
                    {
                        Item = gameState.ItemsDict.GetValueOrNull(characterSchema.Utility2Slot)!,
                        OriginalQuantity = characterSchema.Utility2SlotQuantity,
                        Quantity = characterSchema.Utility2SlotQuantity,
                    }
                );
            }

            ApplyPreFightEffects(characterSchema, potions);

            List<FightSimParticipant> participants =
            [
                new FightSimParticipant
                {
                    Entity = monsterClone,
                    CritCalculator = new DeterministicCritCalculator(
                        monsterClone.CriticalStrike,
                        addedCritChance
                    ),
                    Effects = monsterClone.Effects,
                    IsPlayer = false,
                },
                new FightSimParticipant
                {
                    Entity = characterSchema,
                    CritCalculator = new DeterministicCritCalculator(
                        characterSchema.CriticalStrike,
                        addedCritChance
                    ),
                    Effects = runeEffects,
                    IsPlayer = true,
                },
            ];

            participants.Sort((a, b) => b.Entity.Initiative.CompareTo(a.Entity.Initiative));
            var remainingPlayerHp = playerFullHp ? characterSchema.MaxHp : characterSchema.Hp;
            var remainingMonsterHp = initMonsterHp;

            FightResult? outcome = null;

            int turnNumber = 0;

            int individualTurn = 0;

            CombatLog combatLog = new CombatLog(
                participants.ElementAt(0).Entity,
                participants.ElementAt(1).Entity
            );

            while (outcome is null)
            {
                turnNumber++;

                foreach (var attacker in participants)
                {
                    individualTurn++;
                    // Elaborate for boss fights, e.g. boss will attack different players
                    var defender = participants.FirstOrDefault(participant =>
                        participant.IsPlayer != attacker.IsPlayer
                    )!;

                    List<FightSimUtility> potionEffectsForTurn = attacker.IsPlayer ? potions : [];

                    int poisonDamage = 0;
                    var poison = attacker.Effects.FirstOrDefault(effect =>
                        effect.Code == Effect.Poison
                    );

                    if (poison is not null)
                    {
                        poisonDamage = poison.Value;
                    }

                    /**
                      The poison effect causes x damage per turn, unless the defender has an antidote. If the defender has an antidote,
                      it subtracts the antidote value from the poison, using only 1 antidote.
                    **/
                    if (poison is not null && turnNumber == 1)
                    {
                        poisonDamage = poison.Value;

                        combatLog.Log(
                            individualTurn,
                            attacker.Entity,
                            defender.Entity,
                            $"[{attacker.Entity.Name}] has poison effect for {poisonDamage} damage"
                        );

                        foreach (var potion in potions)
                        {
                            var antidote = potion.Item.Effects.FirstOrDefault(effect =>
                                effect.Code == Effect.Antipoison
                            );

                            if (antidote is not null)
                            {
                                poisonDamage -= antidote.Value;
                                potion.Quantity--;

                                if (poisonDamage < 0)
                                {
                                    poisonDamage = 0;
                                }

                                combatLog.Log(
                                    individualTurn,
                                    attacker.Entity,
                                    defender.Entity,
                                    $"[{attacker.Entity.Name}] has their poison effect mitigated with {antidote.Value} points of antidote - damage is {poisonDamage}"
                                );
                            }
                        }
                    }

                    defender.Entity.Hp -= poisonDamage;

                    if (poisonDamage > 0)
                    {
                        combatLog.Log(
                            individualTurn,
                            attacker.Entity,
                            defender.Entity,
                            $"[{attacker.Entity.Name}] deals {poisonDamage} poison damage"
                        );
                    }

                    ProcessParticipantTurn(
                        new ProcessParticipantTurnParams
                        {
                            Attacker = attacker,
                            OtherAttackers = [],
                            Defender = defender,
                            OtherDefenders = [],
                            IsBossFight = false,
                            AttackerPotionEffects = potionEffectsForTurn,
                            CombatLog = combatLog,
                            TurnNumber = turnNumber,
                            IndividualTurn = individualTurn,
                        }
                    );
                    // ProcessParticipantTurn(
                    //     attacker,
                    //     defender,
                    //     potionEffectsForTurn,
                    //     combatLog,
                    //     turnNumber,
                    //     individualTurn
                    // );

                    bool attackerWon = defender.Entity.Hp <= 0;

                    if (attackerWon)
                    {
                        combatLog.Log(
                            individualTurn,
                            attacker.Entity,
                            defender.Entity,
                            $"[{attacker.Entity.Name}] won."
                        );

                        if (attacker.IsPlayer)
                        {
                            outcome = FightResult.Win;
                            remainingPlayerHp = attacker.Entity.Hp;
                            remainingMonsterHp = defender.Entity.Hp;
                        }
                        else
                        {
                            outcome = FightResult.Loss;
                            remainingMonsterHp = attacker.Entity.Hp;
                            remainingPlayerHp = defender.Entity.Hp;
                        }

                        break;
                    }
                }

                if (i == 0)
                {
                    firstCombatLog = combatLog;
                }
            }

            int potionsUsedInSim = 0;

            foreach (var potion in potions)
            {
                potionsUsedInSim += potion.OriginalQuantity - potion.Quantity;
            }

            outcomes.Add(
                new FightOutcome
                {
                    Result = outcome ?? FightResult.Loss, // Should not be necessary
                    PlayerHp = Math.Min(remainingPlayerHp, originalSchema.MaxHp), // in case of HP boost pots
                    MonsterHp = remainingMonsterHp,
                    TotalTurns = turnNumber,
                    IndvidualTurns = individualTurn,
                    ShouldFight =
                        outcome == FightResult.Win
                        && remainingPlayerHp >= (characterSchema.MaxHp * 0.35)
                        && potionsUsedInSim <= MAX_AMOUNT_OF_USED_POTIONS,
                    PotionsUsed = potionsUsedInSim,
                    FirstSimCombatLog = firstCombatLog!,
                }
            );
        }

        int amountWon = 0;
        int amountShouldFight = 0;

        int playerHp = 0;
        int monsterHp = 0;
        int totalTurns = 0;
        int individualTurns = 0;
        int potionsUsed = 0;

        int fightSimulations = outcomes.Count;

        foreach (var outcome in outcomes)
        {
            amountWon += outcome.Result == FightResult.Win ? 1 : 0;
            amountShouldFight += outcome.ShouldFight == true ? 1 : 0;
            playerHp += outcome.PlayerHp;
            monsterHp += outcome.MonsterHp;
            totalTurns += outcome.TotalTurns;
            individualTurns += outcome.IndvidualTurns;
            potionsUsed += outcome.PotionsUsed;
        }

        playerHp = (int)Math.Floor((double)playerHp / fightSimulations);
        monsterHp = (int)Math.Floor((double)monsterHp / fightSimulations);
        totalTurns = (int)Math.Floor((double)totalTurns / fightSimulations);
        individualTurns = (int)Math.Floor((double)individualTurns / fightSimulations);
        potionsUsed = (int)Math.Floor((double)potionsUsed / fightSimulations);

        bool shouldFight = (amountShouldFight / fightSimulations) > PERCENTAGE_OF_SIMS_TO_WIN;

        FightResult generallyWon =
            (amountWon / fightSimulations) > PERCENTAGE_OF_SIMS_TO_WIN
                ? FightResult.Win
                : FightResult.Loss;

        return new FightOutcome
        {
            Result = generallyWon,
            ShouldFight = shouldFight,
            PlayerHp = playerHp,
            MonsterHp = monsterHp,
            TotalTurns = totalTurns,
            IndvidualTurns = individualTurns,
            PotionsUsed = potionsUsed,
            FirstSimCombatLog = firstCombatLog!,
        };
    }

    private static TurnDamageResult CalculateTurnDamage(
        FightSimParticipant attacker,
        FightSimParticipant defender
    )
    {
        TurnDamageResult result = new()
        {
            ElementalAttacks = [],
            TotalDamage = 0,
            IsCrit = false,
        };

        int resFire = defender.Entity.ResFire;
        int resEarth = defender.Entity.ResEarth;
        int resWater = defender.Entity.ResWater;
        int resAir = defender.Entity.ResAir;

        if (defender.ShellResistanceBoost is not null)
        {
            resFire += defender.ShellResistanceBoost.ResFire;
            resEarth += defender.ShellResistanceBoost.ResEarth;
            resWater += defender.ShellResistanceBoost.ResWater;
            resAir += defender.ShellResistanceBoost.ResAir;
        }

        if (attacker.CritCalculator.CalculateIsCriticalStrike())
        {
            result.IsCrit = true;
        }

        var fireDamage = CalculateElementalAttack(
            attacker.Entity.AttackFire,
            attacker.Entity.DmgFire,
            attacker.Entity.Dmg,
            result.IsCrit,
            resFire
        );

        if (fireDamage > 0)
        {
            result.ElementalAttacks.Add((fireDamage, "fire"));
        }

        var earthDamage = CalculateElementalAttack(
            attacker.Entity.AttackEarth,
            attacker.Entity.DmgEarth,
            attacker.Entity.Dmg,
            result.IsCrit,
            resEarth
        );

        if (earthDamage > 0)
        {
            result.ElementalAttacks.Add((earthDamage, "earth"));
        }

        var waterDamage = CalculateElementalAttack(
            attacker.Entity.AttackWater,
            attacker.Entity.DmgWater,
            attacker.Entity.Dmg,
            result.IsCrit,
            resWater
        );

        if (waterDamage > 0)
        {
            result.ElementalAttacks.Add((waterDamage, "water"));
        }

        var airDamage = CalculateElementalAttack(
            attacker.Entity.AttackAir,
            attacker.Entity.DmgAir,
            attacker.Entity.Dmg,
            result.IsCrit,
            resAir
        );

        if (airDamage > 0)
        {
            result.ElementalAttacks.Add((airDamage, "air"));
        }

        result.TotalDamage = fireDamage + earthDamage + waterDamage + airDamage;

        return result;
    }

    private static int CalculateElementalAttack(
        int baseDamage,
        int elementalMultiplier,
        int damageMultiplier,
        bool isCrit,
        int resistance
    )
    {
        int damage = (int)
            Math.Round(baseDamage + baseDamage * ((elementalMultiplier + damageMultiplier) * 0.01)); // Not sure where the 0.01 is from

        if (isCrit)
        {
            damage = (int)(damage * (1 + CRIT_DAMAGE_MODIFIER));
        }

        damage = (int)Math.Round(damage / (1 + resistance * 0.01));

        return damage;
    }

    public static void ProcessParticipantTurn(ProcessParticipantTurnParams participantTurnParams)
    {
        var attacker = participantTurnParams.Attacker;
        var otherAttackers = participantTurnParams.OtherAttackers;
        var defender = participantTurnParams.Defender;
        var otherDefenders = participantTurnParams.OtherDefenders;
        var isBossFight = participantTurnParams.IsBossFight;

        var attackerPotionEffects = participantTurnParams.AttackerPotionEffects;
        var combatLog = participantTurnParams.CombatLog;
        var turnNumber = participantTurnParams.TurnNumber;
        var individualTurn = participantTurnParams.IndividualTurn;

        // Reset shell if needed
        if (defender.ShellTurnsRemaining > 0)
        {
            defender.ShellTurnsRemaining -= 1;

            if (defender.ShellTurnsRemaining == 0)
            {
                defender.ShellResistanceBoost = null;
            }
        }

        // Count down vampiric strike cooldown
        if (attacker.VampiricStrikeCooldown > 0)
        {
            attacker.VampiricStrikeCooldown -= 1;
        }

        var attack = CalculateTurnDamage(attacker, defender);

        int currentFrenzyValue = attacker.Frenzy;

        // Only lasts one turn
        attacker.Frenzy = 0;

        int damageToDeal = HandleAttackerBurnDamage(
            attacker,
            defender,
            attack,
            combatLog,
            turnNumber,
            individualTurn
        );

        foreach (var potionUtility in attackerPotionEffects)
        {
            if (potionUtility.Quantity < 0)
            {
                continue;
            }

            var restoreEffect = potionUtility.Item.Effects.FirstOrDefault(effect =>
                effect.Code == Effect.Restore
            );

            if (restoreEffect is not null)
            {
                HandleAttackerRestorePotionEffect(
                    attacker,
                    potionUtility,
                    restoreEffect,
                    defender,
                    combatLog,
                    individualTurn
                );
            }

            var splashRestoreEffect = potionUtility.Item.Effects.FirstOrDefault(effect =>
                effect.Code == Effect.SplashRestore
            );

            if (splashRestoreEffect is not null)
            {
                HandleAttackerRestoreSplashPotionEffect(
                    attacker,
                    otherAttackers,
                    potionUtility,
                    splashRestoreEffect,
                    defender,
                    combatLog,
                    individualTurn
                );
            }
        }

        foreach (var (Damage, Element) in attack.ElementalAttacks)
        {
            damageToDeal += Damage;

            combatLog.Log(
                individualTurn,
                attacker.Entity,
                defender.Entity,
                $"[{attacker.Entity.Name}] used {Element} attack and dealt {Damage} damage"
            );

            SimpleEffectSchema? corrupted = defender.Effects.FirstOrDefault(effect =>
                effect.Code == Effect.Corrupted
            );

            if (corrupted is not null)
            {
                int resistance = 0;

                switch (Element)
                {
                    case "air":
                        resistance = defender.Entity.ResAir;
                        break;
                    case "fire":
                        resistance = defender.Entity.ResFire;
                        break;
                    case "water":
                        resistance = defender.Entity.ResWater;
                        break;
                    case "earth":
                        resistance = defender.Entity.ResEarth;
                        break;
                }

                int newResistance = resistance - corrupted.Value;

                switch (Element)
                {
                    case "air":
                        defender.Entity.ResAir = resistance;
                        break;
                    case "fire":
                        defender.Entity.ResFire = resistance;
                        break;
                    case "water":
                        defender.Entity.ResWater = resistance;
                        break;
                    case "earth":
                        defender.Entity.ResEarth = resistance;
                        break;
                }

                combatLog.Log(
                    individualTurn,
                    attacker.Entity,
                    defender.Entity,
                    $"[{defender.Entity.Name}] is corrupted and received {Element} element damage for {Damage} (new resistance is {newResistance})"
                );
            }
        }

        if (currentFrenzyValue > 0)
        {
            int oldDamageToDeal = damageToDeal;
            damageToDeal *= 1 + currentFrenzyValue;

            combatLog.Log(
                individualTurn,
                attacker.Entity,
                defender.Entity,
                $"[{attacker.Entity.Name}] boosting all damage, due to {currentFrenzyValue}% frenzy - total damage for turn is {damageToDeal} (originally {oldDamageToDeal})"
            );
        }

        if (defender.Barrier > 0)
        {
            if (defender.Barrier >= damageToDeal)
            {
                defender.Barrier -= damageToDeal;
                damageToDeal = 0;
            }
            else
            {
                damageToDeal -= defender.Barrier;
                defender.Barrier = 0;
            }
        }

        defender.Entity.Hp -= damageToDeal;

        if (defender.Entity.Hp <= 0)
        {
            return;
        }

        if (
            isBossFight
            && !defender.ShellHasBeenActivated
            && defender.Entity.Hp
                <= defender.Entity.MaxHp * SHELL_ACTIVATION_THRESHOLD_HP_PERCENTAGE
        )
        {
            SimpleEffectSchema? shell = attacker.Effects.FirstOrDefault(effect =>
                effect.Code == Effect.Shell
            );

            if (shell is not null)
            {
                defender.ShellHasBeenActivated = true;
                defender.ShellTurnsRemaining = SHELL_EFFECT_DURATION;

                defender.ShellResistanceBoost = new ResistanceBoost
                {
                    ResAir = shell.Value,
                    ResEarth = shell.Value,
                    ResFire = shell.Value,
                    ResWater = shell.Value,
                };

                combatLog.Log(
                    individualTurn,
                    attacker.Entity,
                    defender.Entity,
                    $"[{defender.Entity.Name}] gains shell effect - {shell.Value}% resistance to all elements"
                );
            }
        }

        if (attack.IsCrit)
        {
            SimpleEffectSchema? lifesteal = attacker.Effects.FirstOrDefault(effect =>
                effect.Code == Effect.Lifesteal
            );

            if (lifesteal is not null)
            {
                // We use the raw damage here, don't think lifesteal works with burn
                int heal = (int)Math.Round(attack.TotalDamage * lifesteal.Value * 0.01);

                heal = GetAmountToHeal(heal, attacker.Entity.Hp, attacker.Entity.MaxHp);

                attacker.Entity.Hp += heal;

                combatLog.Log(
                    individualTurn,
                    attacker.Entity,
                    defender.Entity,
                    $"[{attacker.Entity.Name}] heals {heal} HP from Life steal effect"
                );
            }

            if (isBossFight)
            {
                SimpleEffectSchema? vampiricStrike = attacker.Effects.FirstOrDefault(effect =>
                    effect.Code == Effect.VampiricStrike
                );

                if (vampiricStrike is not null && attacker.VampiricStrikeCooldown == 0)
                {
                    // We use the raw damage here, don't think lifesteal works with burn
                    int heal = (int)Math.Round(attack.TotalDamage * vampiricStrike.Value * 0.01);

                    FightSimParticipant? attackerWithLowestHp = null;

                    foreach (var otherAttacker in otherAttackers)
                    {
                        if (
                            attackerWithLowestHp is null
                            || otherAttacker.Entity.Hp < attackerWithLowestHp.Entity.Hp
                        )
                        {
                            attackerWithLowestHp = otherAttacker;
                        }
                    }

                    if (attackerWithLowestHp is not null)
                    {
                        heal = GetAmountToHeal(
                            heal,
                            attackerWithLowestHp.Entity.Hp,
                            attackerWithLowestHp.Entity.MaxHp
                        );

                        attackerWithLowestHp.Entity.Hp += heal;

                        combatLog.Log(
                            individualTurn,
                            attacker.Entity,
                            defender.Entity,
                            $"[{attacker.Entity.Name}] heals {attackerWithLowestHp.Entity.Name} {heal} HP from Vampiric strike effect"
                        );

                        attacker.VampiricStrikeCooldown = VAMPIRIC_STRIKE_COOLDOWN_TURNS;
                    }
                }
            }

            SimpleEffectSchema? frenzy = attacker.Effects.FirstOrDefault(effect =>
                effect.Code == Effect.Frenzy
            );

            if (frenzy is not null)
            {
                attacker.Frenzy = frenzy.Value;

                combatLog.Log(
                    individualTurn,
                    attacker.Entity,
                    attacker.Entity,
                    $"[{attacker.Entity.Name}] got Frenzy from critting - {frenzy.Value}% damage boost for them and their allies until next turn"
                );

                if (isBossFight)
                {
                    foreach (var otherAttacker in otherAttackers)
                    {
                        if (otherAttacker.Frenzy < frenzy.Value)
                        {
                            combatLog.Log(
                                individualTurn,
                                attacker.Entity,
                                defender.Entity,
                                $"[{attacker.Entity.Name}] grants Frenzy to {otherAttacker.Entity.Name} - {frenzy.Value}% damage boost until next turn"
                            );
                        }
                    }
                }
            }
        }

        if (isBossFight && turnNumber % 2 == 0)
        {
            SimpleEffectSchema? healingAura = attacker.Effects.FirstOrDefault(effect =>
                effect.Code == Effect.HealingAura
            );

            if (healingAura is not null)
            {
                foreach (var otherAttacker in otherAttackers)
                {
                    int amountToHeal = (int)
                        Math.Round(otherAttacker.Entity.MaxHp * (healingAura.Value * 0.01));

                    amountToHeal = GetAmountToHeal(
                        amountToHeal,
                        otherAttacker.Entity.Hp,
                        otherAttacker.Entity.MaxHp
                    );

                    otherAttacker.Entity.Hp += amountToHeal;

                    combatLog.Log(
                        individualTurn,
                        attacker.Entity,
                        defender.Entity,
                        $"[{attacker.Entity.Name}] heals {amountToHeal} for {otherAttacker.Entity.Name} from Healing aura effect"
                    );
                }
            }
        }

        if (turnNumber % 3 == 0)
        {
            SimpleEffectSchema? heal = attacker.Effects.FirstOrDefault(effect =>
                effect.Code == Effect.Healing
            );

            if (heal is not null)
            {
                int amountToHeal = (int)Math.Round(attacker.Entity.MaxHp * (heal.Value * 0.01));

                amountToHeal = GetAmountToHeal(
                    amountToHeal,
                    attacker.Entity.Hp,
                    attacker.Entity.MaxHp
                );

                attacker.Entity.Hp += amountToHeal;

                combatLog.Log(
                    individualTurn,
                    attacker.Entity,
                    defender.Entity,
                    $"[{attacker.Entity.Name}] heals {amountToHeal} from Healing effect"
                );
            }
        }

        if (turnNumber % 5 == 0)
        {
            SimpleEffectSchema? barrier = attacker.Effects.FirstOrDefault(effect =>
                effect.Code == Effect.Barrier
            );

            if (barrier is not null)
            {
                attacker.Barrier = barrier.Value;

                combatLog.Log(
                    individualTurn,
                    attacker.Entity,
                    defender.Entity,
                    $"[{attacker.Entity.Name}] gains a barrier defending {barrier.Value} HP"
                );
            }
        }

        if (turnNumber % 20 == 0)
        {
            SimpleEffectSchema? reconstitution = attacker.Effects.FirstOrDefault(effect =>
                effect.Code == Effect.Reconstitution
            );

            if (reconstitution is not null)
            {
                attacker.Entity.Hp = attacker.Entity.MaxHp;
                combatLog.Log(
                    individualTurn,
                    attacker.Entity,
                    defender.Entity,
                    $"[{attacker.Entity.Name}] heals to max HP {attacker.Entity.MaxHp} from Reconstitution effect"
                );
            }
        }
    }

    public static FightSimResult FindBestFightEquipment(
        PlayerCharacter character,
        GameState gameState,
        MonsterSchema monster,
        List<ItemInInventory>? allItems = null,
        List<string>? itemTypesToSim = null
    )
    {
        if (allItems is null)
        {
            allItems = GetItemsFromInventoryForSim(character.Schema, gameState);
        }

        allItems = allItems
            .Where(item => ItemService.CanUseItem(item.Item, character.Schema))
            .ToList();

        allItems = GetItemsWorthSimming(allItems);

        // This order should matter somewhat, since e.g. body armor slots typically give more stats than items lower in the list, e.g boots and amulets.
        // List<EquipmentTypeMapping> equipmentTypes = allEquipmentTypes;
        List<EquipmentTypeMapping> tempEquipmentTypes = [];

        if (itemTypesToSim is null)
        {
            tempEquipmentTypes = allEquipmentTypes;
        }
        else
        {
            tempEquipmentTypes = allEquipmentTypes
                .Where(type => itemTypesToSim.Contains(type.ItemType))
                .ToList();
        }

        List<EquipmentTypeMapping> potionEquipmentTypes = [];

        List<EquipmentTypeMapping> nonWeaponEquipmentTypes = [];

        foreach (var equipmentType in tempEquipmentTypes)
        {
            if (equipmentType.ItemType == "utility")
            {
                potionEquipmentTypes.Add(equipmentType);
            }
            else if (equipmentType.ItemType != "weapon")
            {
                nonWeaponEquipmentTypes.Add(equipmentType);
            }
        }

        /*
          This might not be the most optimal, but basically we go through each item type one by one, and find the best fit for every item to equip.
          There are definitely cases we don't handle super well by doing this, because the characer might have a fire weapon, that will be better
          with a specific armor set, because it gives more fire damage, but we will never consider that scenario, because the fire weapon might be
          disqualified in the "weapon" round, because it's not the best item.
          
          I think a good middleway is to always calculate all weapons, but for each weapon just find the best of each candidate.
          That means we don't loop through all possible combinations of all items, but we find the best equipment set with each item,
          which can handle that the air weapon might be best with the +air dmg set.
        */

        // var originalSchemaDontTouch = character.Schema with
        // { };

        var initialSchema = character.Schema with
        { };

        initialSchema.Hp = initialSchema.MaxHp;

        // We essentially remove the potions from the sim, until they naturally will be simulated.

        if (!string.IsNullOrWhiteSpace(initialSchema.Utility1Slot))
        {
            allItems.Add(
                new ItemInInventory
                {
                    Item = gameState.ItemsDict[initialSchema.Utility1Slot],
                    Quantity = 100,
                }
            );
            initialSchema.Utility1Slot = "";
            initialSchema.Utility1SlotQuantity = 0;
        }

        if (!string.IsNullOrWhiteSpace(initialSchema.Utility2Slot))
        {
            allItems.Add(
                new ItemInInventory
                {
                    Item = gameState.ItemsDict[initialSchema.Utility2Slot],
                    Quantity = 100,
                }
            );
            initialSchema.Utility2Slot = "";
            initialSchema.Utility2SlotQuantity = 0;
        }

        string initialWeaponCode = initialSchema.WeaponSlot;

        var initialFightOutcome = CalculateFightOutcome(initialSchema, monster, gameState);

        var weapons = allItems
            .Where(item => item.Item.Type == "weapon" && item.Item.Subtype != "tool")
            .ToList();

        if (
            !string.IsNullOrWhiteSpace(initialSchema.WeaponSlot)
            && !weapons.Exists(item => item.Item.Code == initialSchema.WeaponSlot)
        )
        {
            weapons.Add(
                new ItemInInventory
                {
                    Item = gameState.ItemsDict[initialSchema.WeaponSlot],
                    Quantity = 1,
                }
            );
        }

        List<FightSimResult> allCandidates = [];

        allCandidates.Add(
            new FightSimResult
            {
                Schema = initialSchema,
                Outcome = initialFightOutcome,
                ItemsToEquip = [],
            }
        );

        foreach (var weapon in weapons)
        {
            var bestSchemaCandiateWithWeapon = initialSchema with { };

            bestSchemaCandiateWithWeapon.Hp = bestSchemaCandiateWithWeapon.MaxHp;

            bestSchemaCandiateWithWeapon = PlayerActionService.SimulateItemEquip(
                bestSchemaCandiateWithWeapon,
                gameState.ItemsDict.GetValueOrNull(bestSchemaCandiateWithWeapon.WeaponSlot),
                gameState.ItemsDict.GetValueOrNull(weapon.Item.Code)!,
                "WeaponSlot",
                1
            );

            var bestFightOutcomeWithWeapon = CalculateFightOutcome(
                bestSchemaCandiateWithWeapon,
                monster,
                gameState
            );

            List<EquipmentSlot> itemsToEquip = [];

            if (weapon.Item.Code != initialWeaponCode)
            {
                itemsToEquip.Add(
                    new EquipmentSlot
                    {
                        Code = weapon.Item.Code,
                        Quantity = 1,
                        Slot = "weapon",
                    }
                );
            }

            var bestFightSimResult = new FightSimResult
            {
                Schema = bestSchemaCandiateWithWeapon,
                Outcome = bestFightOutcomeWithWeapon,
                ItemsToEquip = itemsToEquip,
            };

            foreach (var equipmentTypeMapping in nonWeaponEquipmentTypes)
            {
                var result = SimItemsForEquipmentType(
                    character,
                    gameState,
                    monster,
                    allItems,
                    equipmentTypeMapping,
                    bestFightSimResult
                );

                bestFightSimResult.Schema = result.Schema;
                bestFightSimResult.Outcome = result.Outcome;
                bestFightSimResult.ItemsToEquip = itemsToEquip.Union(result.ItemsToEquip).ToList();

                // Bit of a hack, but we have to keep track of quantity
                if (equipmentTypeMapping.ItemType == "ring")
                {
                    allItems.ForEach(item =>
                    {
                        var matchingItem = itemsToEquip.FirstOrDefault(itemToEquip =>
                            itemToEquip.Code == item.Item.Code
                        );

                        if (matchingItem is not null)
                        {
                            item.Quantity -= matchingItem.Quantity;
                        }
                    });
                }
            }
            var potionEffectsToSkip = EffectService.GetPotionEffectsToSkip(
                bestSchemaCandiateWithWeapon,
                monster
            );

            // Sim potions afterwards
            foreach (var equipmentTypeMapping in potionEquipmentTypes)
            {
                var result = SimItemsForEquipmentType(
                    character,
                    gameState,
                    monster,
                    allItems.FindAll(item =>
                        !item.Item.Effects.Exists(effect =>
                            potionEffectsToSkip.Contains(effect.Code)
                        )
                    ),
                    equipmentTypeMapping,
                    bestFightSimResult
                );

                bestFightSimResult.Schema = result.Schema;
                bestFightSimResult.Outcome = result.Outcome;
                bestFightSimResult.ItemsToEquip = itemsToEquip.Union(result.ItemsToEquip).ToList();
            }

            allCandidates.Add(bestFightSimResult);
        }

        allCandidates.Sort(
            (a, b) =>
            {
                int sortValue = CompareSimOutcome(a.Outcome, b.Outcome);

                if (sortValue == 0)
                {
                    return a.ItemsToEquip.Count < b.ItemsToEquip.Count ? -1 : 1;
                }
                else
                {
                    return sortValue;
                }
            }
        );

        // character.Schema = originalSchemaDontTouch;

        return allCandidates.ElementAt(0);
    }

    /**
      * Fight sim, where we assume the character can obtain the needed potions, even if they don't have them currently
    **/
    public static FightSimResult FindBestFightEquipmentWithUsablePotions(
        PlayerCharacter character,
        GameState gameState,
        MonsterSchema monster,
        List<ItemInInventory>? allItems = null
    )
    {
        var allPotions = gameState
            .UtilityItemsDict.Select(item => new ItemInInventory
            {
                Item = item.Value,
                Quantity = 100,
            })
            .ToList();

        var itemsInInventoryForSimming = GetItemsFromInventoryForSim(character.Schema, gameState);

        var itemCandidates = allPotions.Union(itemsInInventoryForSimming).ToList();

        if (allItems is not null)
        {
            itemCandidates = itemCandidates.Union(allItems).ToList();
        }

        return FindBestFightEquipment(character, gameState, monster, itemCandidates);
    }

    public static FightSimResult SimItemsForEquipmentType(
        PlayerCharacter originalCharacter,
        GameState gameState,
        MonsterSchema monster,
        List<ItemInInventory> allItems,
        EquipmentTypeMapping equipmentTypeMapping,
        FightSimResult originalResult
    )
    {
        /*
        This might not be the most optimal, but basically we go through each item type one by one, and find the best fit for every item to equip.
        There are definitely cases we don't handle super well by doing this, because the characer might have a fire weapon, that will be better
        with a specific armor set, because it gives more fire damage, but we will never consider that scenario, because the fire weapon might be
        disqualified in the "weapon" round, because it's not the best item.
        
        I think a good middleway is to always calculate all weapons, but for each weapon just find the best of each candidate.
        That means we don't loop through all possible combinations of all items, but we find the best equipment set with each item,
        which can handle that the air weapon might be best with the +air dmg set.
        */

        var bestSchemaCandidate = originalResult.Schema with
        { };
        var bestFightOutcome = originalResult.Outcome with { };
        var itemsToEquip = originalResult.ItemsToEquip.Select(item => item).ToList();

        bestSchemaCandidate.Hp = bestSchemaCandidate.MaxHp;

        int bestItemAmount = 1;

        var equipmentType = equipmentTypeMapping.ItemType;
        var equipmentSlot = equipmentTypeMapping.Slot;
        // var items = character.GetItemsFromInventoryWithType(equipmentType);
        var items = allItems
            .Where(item => item.Item.Type == equipmentType && item.Quantity > 0)
            .ToList();

        if (items.Count == 0)
        {
            return new FightSimResult
            {
                Schema = bestSchemaCandidate,
                Outcome = bestFightOutcome,
                ItemsToEquip = itemsToEquip,
            };
        }

        var character = originalCharacter.Clone();

        EquipmentSlot? equippedItem = character.GetEquipmentSlot(equipmentSlot);

        ItemSchema? bestItemCandidate = equippedItem is not null
            ? gameState.ItemsDict.GetValueOrNull(equippedItem.Code)
            : null;

        string? initialItemCode = bestItemCandidate?.Code;

        foreach (var item in items)
        {
            ItemSchema? itemSchema = gameState.ItemsDict.GetValueOrNull(item.Item.Code);

            if (itemSchema is null)
            {
                throw new Exception(
                    $"Current weapon with code \"{item.Item.Code}\" is null - should never happen"
                );
            }

            if (itemSchema.Type == "artifact")
            {
                bool alreadyHasArtifactEquipped = ItemService.AreArtifactsOverlapping(
                    item.Item.Code,
                    bestSchemaCandidate
                );

                if (alreadyHasArtifactEquipped)
                {
                    continue;
                }
            }

            if (itemSchema.Subtype == "tool")
            {
                continue;
            }

            if (!ItemService.CanUseItem(itemSchema, character.Schema))
            {
                continue;
            }

            // Not sure, but I don't think you can have the same effect in both util slots.
            if (item.Item.Type == "utility")
            {
                string otherItemSlot = (
                    equipmentSlot == "Utility1Slot"
                        ? bestSchemaCandidate.Utility2Slot
                        : bestSchemaCandidate.Utility1Slot
                );

                if (
                    ItemService.ArePotionEffectsOverlapping(
                        gameState,
                        item.Item.Code,
                        otherItemSlot
                    )
                )
                {
                    continue;
                }
            }

            var characterSchema = bestSchemaCandidate with { };

            characterSchema = PlayerActionService.SimulateItemEquip(
                characterSchema,
                bestItemCandidate,
                itemSchema,
                equipmentSlot,
                item.Quantity
            );

            var fightOutcome = CalculateFightOutcome(characterSchema, monster, gameState);

            int simOutcome = CompareSimOutcome(bestFightOutcome, fightOutcome);

            bool fightOutcomeIsBetter = simOutcome == 1;

            if (fightOutcomeIsBetter)
            {
                /**
                 *
                 * A worse item setup might have us use character HP pots earlier,
                 * which can end up with them having a higher amount of * remaining HP,
                 * but they also used more potions.
                 *
                 * Essentially we don't want to choose to use more potions, if we already can beat the monster without them.
                 *
                 * This logic shouldn't be necessary anymore, as we simulate unequipping potions before simming equipment,
                 * and then sim the potions after, so the potions won't affect the equipment choice.
                 *
                **/
                if (
                    equipmentTypeMapping.ItemType != "utility"
                    && fightOutcome.PotionsUsed > bestFightOutcome.PotionsUsed
                )
                {
                    continue;
                }

                bestFightOutcome = fightOutcome;
                bestItemCandidate = item.Item;
                bestSchemaCandidate = characterSchema;
                bestItemAmount = item.Item.Type == "utility" ? item.Quantity : 1;
            }
        }

        if (bestItemCandidate is not null && initialItemCode != bestItemCandidate.Code)
        {
            string snakeCaseSlot = equipmentSlot.Replace("Slot", "").FromPascalToSnakeCase();

            logger.LogDebug(
                $"FindBestFightEquipment: Should swap \"{initialItemCode}\" -> \"{bestItemCandidate.Code}\" in slot \"{snakeCaseSlot}\" for {character.Schema.Name} when fighting \"{monster.Code}\""
            );

            itemsToEquip.Add(
                new EquipmentSlot
                {
                    Code = bestItemCandidate.Code,
                    Slot = snakeCaseSlot,
                    Quantity = bestItemAmount,
                }
            );
        }

        return new FightSimResult
        {
            Schema = bestSchemaCandidate,
            Outcome = bestFightOutcome,
            ItemsToEquip = itemsToEquip,
        };
    }

    public static int CompareSimOutcome(FightOutcome a, FightOutcome b)
    {
        int aWinsValue = -1;
        int bWinsValue = 1;

        if (a.Result == FightResult.Win && b.Result == FightResult.Loss)
        {
            return aWinsValue;
        }

        if (a.Result == FightResult.Loss && b.Result == FightResult.Win)
        {
            return bWinsValue;
        }

        if (a.ShouldFight && !b.ShouldFight)
        {
            return aWinsValue;
        }

        if (!a.ShouldFight && b.ShouldFight)
        {
            return bWinsValue;
        }

        // It's only if we are winning that we care about amount of turns - if we are losing,
        // it could mean that we have good survivability
        if (a.Result == FightResult.Win && b.Result == FightResult.Win)
        {
            if (a.IndvidualTurns < b.IndvidualTurns)
            {
                return aWinsValue;
            }

            if (a.IndvidualTurns > b.IndvidualTurns)
            {
                return bWinsValue;
            }

            if (a.PlayerHp > b.PlayerHp)
            {
                return aWinsValue;
            }

            if (a.PlayerHp < b.PlayerHp)
            {
                return bWinsValue;
            }

            if (a.MonsterHp < b.MonsterHp)
            {
                return aWinsValue;
            }

            if (a.MonsterHp > b.MonsterHp)
            {
                return bWinsValue;
            }
        }
        else
        {
            if (a.MonsterHp < b.MonsterHp)
            {
                return aWinsValue;
            }

            if (a.MonsterHp > b.MonsterHp)
            {
                return bWinsValue;
            }

            if (a.PlayerHp > b.PlayerHp)
            {
                return aWinsValue;
            }

            if (a.PlayerHp < b.PlayerHp)
            {
                return bWinsValue;
            }
        }

        return 0;
    }

    static void ApplyPreFightEffects(CharacterSchema characterSchema, List<FightSimUtility> potions)
    {
        foreach (var potion in potions)
        {
            foreach (var effect in potion.Item.Effects)
            {
                if (EffectService.preFightEffects.Contains(effect.Code))
                {
                    // We assume that all prefight effects are positive things, so we apply them to the character
                    EffectService.ApplyEffect(characterSchema, effect);

                    potion.Quantity--;
                }
            }
        }
    }

    public static List<MonsterSchema> GetRelevantMonstersForCharacter(
        PlayerCharacter character,
        GameState gameState
    )
    {
        int playerLevel = character.Schema.Level;

        List<MonsterSchema> mediumMonsters = [];

        List<MonsterSchema> toughMonsters = [];

        int lowerLevelBound = playerLevel - 6;

        if (lowerLevelBound < 0)
        {
            lowerLevelBound = 1;
        }

        int upperLevelBound = playerLevel + 3;

        if (upperLevelBound > MAX_LEVEL)
        {
            upperLevelBound = MAX_LEVEL;
        }

        // Find the most difficult monsters that the character should realistically be able to fight,
        // at their current level. It's okay that the character cannot defeat them all at the moment.
        //

        var filteredMonsters = gameState
            .AvailableMonsters.Where(
                (monster) =>
                {
                    return monster.Type != MonsterType.Boss
                        && monster.Level >= lowerLevelBound
                        && monster.Level <= upperLevelBound;
                }
            )
            .ToList();

        filteredMonsters.Sort((a, b) => b.Level - a.Level);

        return filteredMonsters.GetRange(0, Math.Min(5, filteredMonsters.Count - 1));
    }

    public static HashSet<string> GetItemsRelevantMonsters(
        PlayerCharacter character,
        GameState gameState,
        List<ItemInInventory> items,
        bool includeItemsFromInventory
    )
    {
        HashSet<string> relevantItems = [];

        var relevantMonsters = GetRelevantMonstersForCharacter(character, gameState);

        List<ItemInInventory> itemsToUse = [.. items];

        if (includeItemsFromInventory)
        {
            itemsToUse = character
                .Schema.Inventory.Where(item => !string.IsNullOrEmpty(item.Code))
                .Select(item => new ItemInInventory
                {
                    Item = gameState.ItemsDict[item.Code],
                    Quantity = item.Quantity,
                })
                .Union(items)
                .ToList();
        }

        foreach (var monster in relevantMonsters)
        {
            var bestFightItems = (
                FindBestFightEquipment(character, gameState, monster, itemsToUse)
            ).ItemsToEquip;

            foreach (var item in bestFightItems)
            {
                relevantItems.Add(item.Code);
            }
        }

        return relevantItems;
    }

    /**
      * Essentially only take the best of the items, sorting out linear downgrades. If two items have the same effects, only keep the best one
    **/
    public static List<ItemInInventory> GetItemsWorthSimming(List<ItemInInventory> items)
    {
        Dictionary<string, ItemInInventory> resultList = [];

        Dictionary<string, List<ItemInInventory>> slotItemDict = [];

        foreach (var item in items)
        {
            if (
                !allEquipmentTypes.Exists(type => type.ItemType == item.Item.Type)
                || item.Item.Subtype == "tool"
            )
            {
                continue;
            }

            if (slotItemDict.ContainsKey(item.Item.Type))
            {
                slotItemDict[item.Item.Type].Add(item);
            }
            else
            {
                slotItemDict.Add(item.Item.Type, [item]);
            }
        }

        foreach (var keyValuePair in slotItemDict)
        {
            List<ItemInInventory> itemsToKeep = [];

            List<string> skipList = [];

            foreach (var item in keyValuePair.Value)
            {
                if (skipList.Contains(item.Item.Code))
                {
                    continue;
                }

                itemsToKeep.Add(item);

                foreach (var itemToCompareTo in keyValuePair.Value)
                {
                    if (item.Item.Code == itemToCompareTo.Item.Code)
                    {
                        continue;
                    }

                    var result = ItemService.GetBestItemIfUpgrade(item.Item, itemToCompareTo.Item);

                    if (result is null)
                    {
                        // They don't overlap
                        itemsToKeep.Add(itemToCompareTo);
                        continue;
                    }

                    if (result is not null)
                    {
                        if (result.Code == item.Item.Code)
                        {
                            itemsToKeep.Add(item);
                            skipList.Add(itemToCompareTo.Item.Code);
                        }
                        else
                        {
                            itemsToKeep.Add(itemToCompareTo);
                            skipList.Add(result.Code);
                            break; // the other item is better, so disqualify the current one
                        }
                    }
                }
            }

            var filteredItems = itemsToKeep
                .Where(item => !skipList.Contains(item.Item.Code))
                .ToList();

            foreach (var item in filteredItems)
            {
                resultList.TryAdd(item.Item.Code, item);
            }
        }

        return resultList.Select(element => element.Value).ToList();
    }

    public static List<ItemInInventory> GetItemsFromInventoryForSim(
        CharacterSchema characterSchema,
        GameState gameState
    )
    {
        return characterSchema
            .Inventory.Where(item => !string.IsNullOrEmpty(item.Code))
            .Select(item => new ItemInInventory
            {
                Item = gameState.ItemsDict[item.Code],
                Quantity = item.Quantity,
            })
            .ToList();
    }

    public static int GetAmountToHeal(int healEffect, int entityHp, int entityMaxHp)
    {
        int amountToHeal = Math.Min(healEffect, entityMaxHp - entityHp);

        return amountToHeal;
    }

    static int HandleAttackerBurnDamage(
        FightSimParticipant attacker,
        FightSimParticipant defender,
        TurnDamageResult attack,
        CombatLog combatLog,
        int turnNumber,
        int individualTurn
    )
    {
        SimpleEffectSchema? burn = attacker.Effects.FirstOrDefault(effect =>
            effect.Code == Effect.Burn
        );

        if (burn is null)
        {
            return 0;
        }

        int burnDamage = 0;

        if (turnNumber == 1)
        {
            /**
            ** Calculate the initial burn damage - all subsequent turns will use this intial damage,
            ** but 10% is removed each turn
            */
            double burnFactor = burn.Value * 0.01;

            burnDamage = (int)Math.Round(attack.TotalDamage * burnFactor);
        }
        else
        {
            // Damage decreases by 10% each turn
            burnDamage = (int)Math.Round(attacker.BurnDamageForNextTurn * 0.9);
        }

        if (burnDamage < 0)
        {
            // Should never happen
            burnDamage = 0;
        }

        // Update for next turn
        attacker.BurnDamageForNextTurn = burnDamage;

        combatLog.Log(
            individualTurn,
            attacker.Entity,
            defender.Entity,
            $"[{attacker.Entity.Name}] deals {burnDamage} burn damage to {defender.Entity.Name}"
        );

        // defender.Entity.Hp -= burnDamage;
        return burnDamage;
    }

    static void HandleAttackerRestorePotionEffect(
        FightSimParticipant attacker,
        FightSimUtility fightSimEffect,
        SimpleEffectSchema restoreEffect,
        FightSimParticipant defender,
        CombatLog combatLog,
        int individualTurn
    )
    {
        if (attacker.Entity.Hp <= attacker.Entity.MaxHp * RESTORE_EFFECT_MAX_HP_THRESHOLD)
        {
            int amountHealed = GetAmountToHeal(
                restoreEffect.Value,
                attacker.Entity.Hp,
                attacker.Entity.MaxHp
            );

            attacker.Entity.Hp += amountHealed;

            fightSimEffect.Quantity--;

            combatLog.Log(
                individualTurn,
                attacker.Entity,
                defender.Entity,
                $"[{attacker.Entity.Name}] heals {amountHealed} from a health potion"
            );
        }
    }

    static void HandleAttackerRestoreSplashPotionEffect(
        FightSimParticipant attacker,
        List<FightSimParticipant> otherAttackers,
        FightSimUtility fightSimEffect,
        SimpleEffectSchema splashRestoreEffect,
        FightSimParticipant defender,
        CombatLog combatLog,
        int individualTurn
    )
    {
        FightSimParticipant? attackerWithLowestHp = null;

        foreach (var otherAttacker in otherAttackers)
        {
            if (
                attackerWithLowestHp is null
                || otherAttacker.Entity.Hp < attackerWithLowestHp.Entity.Hp
            )
            {
                attackerWithLowestHp = otherAttacker;
            }
        }

        if (
            attackerWithLowestHp is not null
            && attackerWithLowestHp.Entity.Hp
                <= attackerWithLowestHp.Entity.MaxHp * RESTORE_EFFECT_MAX_HP_THRESHOLD
        )
        {
            int amountHealed = GetAmountToHeal(
                splashRestoreEffect.Value,
                attackerWithLowestHp.Entity.Hp,
                attackerWithLowestHp.Entity.MaxHp
            );

            attackerWithLowestHp.Entity.Hp += amountHealed;

            fightSimEffect.Quantity--;

            combatLog.Log(
                individualTurn,
                attacker.Entity,
                defender.Entity,
                $"[{attacker.Entity.Name}] heals {attackerWithLowestHp.Entity.Name} {amountHealed} HP from a health splash potion"
            );
        }
    }
}

public record FightOutcome
{
    public FightResult Result { get; init; }

    public int PlayerHp { get; init; }

    public int MonsterHp { get; init; }

    public int TotalTurns { get; init; }
    public int IndvidualTurns { get; init; }

    public bool ShouldFight { get; init; }

    public int PotionsUsed { get; set; } = 0;

    public required CombatLog FirstSimCombatLog { get; set; }
}

public record FightSimUtility
{
    public required ItemSchema Item { get; set; }

    public int OriginalQuantity { get; set; }
    public int Quantity { get; set; }
}

public record EquipmentTypeMapping
{
    public string ItemType { get; set; } = "";
    public string Slot { get; set; } = "";
}

public record FightSimResult
{
    public required CharacterSchema Schema { get; set; }
    public required FightOutcome Outcome { get; set; }
    public required List<EquipmentSlot> ItemsToEquip { get; set; }
}

public record TurnDamageResult
{
    public required List<(int Damage, string Elemental)> ElementalAttacks { get; set; }
    public required int TotalDamage { get; set; }

    public required bool IsCrit { get; set; } = false;
}

public record FightSimParticipant
{
    public required FightEntity Entity { get; set; }

    public int Barrier { get; set; } = 0;
    public int Frenzy { get; set; } = 0;
    public bool ShellHasBeenActivated { get; set; } = false;
    public int ShellTurnsRemaining { get; set; } = 0;
    public ResistanceBoost? ShellResistanceBoost { get; set; }

    public int VampiricStrikeCooldown { get; set; } = 0;

    public int BurnDamageForNextTurn { get; set; } = 0;
    public required ICritCalculator CritCalculator { get; set; }
    public required List<SimpleEffectSchema> Effects { get; set; }
    public required bool IsPlayer { get; set; }
}

public record ProcessParticipantTurnParams
{
    public required FightSimParticipant Attacker { get; set; }
    public required List<FightSimParticipant> OtherAttackers { get; set; }
    public required FightSimParticipant Defender { get; set; }
    public required List<FightSimParticipant> OtherDefenders { get; set; }
    public required bool IsBossFight { get; set; }
    public required List<FightSimUtility> AttackerPotionEffects { get; set; }
    public required CombatLog CombatLog { get; set; }
    public required int TurnNumber { get; set; }
    public required int IndividualTurn { get; set; }
}

public record ResistanceBoost
{
    public int ResFire { get; set; } = 0;

    public int ResEarth { get; set; } = 0;

    public int ResWater { get; set; } = 0;

    public int ResAir { get; set; } = 0;
}
