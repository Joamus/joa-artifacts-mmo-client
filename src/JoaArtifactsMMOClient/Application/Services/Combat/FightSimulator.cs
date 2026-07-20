using Application;
using Application.ArtifactsApi.Schemas;
using Application.Character;
using Application.Errors;
using Application.Records;
using Application.Services;
using Application.Services.Combat;
using Microsoft.OpenApi.Extensions;

namespace Applicaton.Services.FightSimulator;

public static class FightSimulator
{
    private static readonly int MAX_LEVEL = 50;
    public const double PERCENTAGE_OF_SIMS_TO_WIN = 0.85;
    private const float SHOULD_FIGHT_MAX_HP_PLAYER_THRESHOLD = 0.35f;
    private const int MAX_AMOUNT_OF_USED_POTIONS = 10;
    private const double CRIT_DAMAGE_MODIFIER = 0.5;
    private const double RESTORE_EFFECT_MAX_HP_THRESHOLD = 0.50;
    private const double LOSE_AFTER_TURNS = 100;
    private const int CHANCE_OF_BOSS_SWITCH_TARGET = 10;

    private const int SHELL_EFFECT_DURATION = 3;
    private const int VAMPIRIC_STRIKE_COOLDOWN_TURNS = 3;
    private const int SHELL_ACTIVATION_THRESHOLD_HP_PERCENTAGE = 40;

    private static ILogger Logger = AppLogger.GetLogger();

    public static FightSimResultWithLeftOverItems FindBestFightEquipmentWithUsablePotions(
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

        var itemsInInventoryForSimming = FightSimulator.GetItemsFromInventoryForSim(
            character.Schema,
            gameState
        );

        var itemCandidates = allPotions.Union(itemsInInventoryForSimming).ToList();

        if (allItems is not null)
        {
            itemCandidates = itemCandidates.Union(allItems).ToList();
        }

        return FightSimulator.FindBestFightEquipment(character, gameState, monster, itemCandidates);
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
            var bestFightItems = FightSimulator
                .FindBestFightEquipment(character, gameState, monster, itemsToUse)
                .SimResult.ItemsToEquip;

            foreach (var item in bestFightItems)
            {
                relevantItems.Add(item.Code);
            }
        }

        return relevantItems;
    }

    /**
     * We just rotate the resistances, it shouldn't really matter too much
    */
    public static ResistanceBoost GetProtectiveBubble(
        FightSimParticipant defender,
        int protectiveBubbleValue
    )
    {
        var currentProtectiveBubble =
            defender.ProtectiveBubble
            ?? new ResistanceBoost
            {
                ResFire = 0,
                ResEarth = 0,
                ResWater = 0,
                ResAir = 0,
            };

        return currentProtectiveBubble switch
        {
            { ResFire: var resFire } when resFire > 0 => new ResistanceBoost
            {
                ResFire = 0,
                ResEarth = protectiveBubbleValue,
                ResWater = 0,
                ResAir = 0,
            },
            { ResEarth: var ResEarth } when ResEarth > 0 => new ResistanceBoost
            {
                ResFire = 0,
                ResEarth = 0,
                ResWater = protectiveBubbleValue,
                ResAir = 0,
            },
            { ResWater: var resWater } when resWater > 0 => new ResistanceBoost
            {
                ResFire = 0,
                ResEarth = 0,
                ResWater = resWater,
                ResAir = 0,
            },
            { ResAir: var resAir } when resAir > 0 => new ResistanceBoost
            {
                ResFire = 0,
                ResEarth = 0,
                ResWater = 0,
                ResAir = protectiveBubbleValue,
            },
            // Case for first turn
            _ => new ResistanceBoost
            {
                ResFire = protectiveBubbleValue,
                ResEarth = 0,
                ResWater = 0,
                ResAir = 0,
            },
        };
    }

    public static FightSimParticipant BuildPlayerFightSimParticipant(
        CharacterSchema originalCharacterSchema,
        GameState gameState,
        bool playerFullHp,
        int addedCritChance
    )
    {
        List<FightSimUtility> potions = [];

        CharacterSchema characterSchema = originalCharacterSchema with { };

        if (playerFullHp)
        {
            characterSchema.Hp = characterSchema.MaxHp;
        }

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

        return new FightSimParticipant
        {
            Entity = characterSchema,
            OriginalHp = originalCharacterSchema.Hp,
            OriginalMaxHp = originalCharacterSchema.MaxHp,
            CritCalculator = new DeterministicCritCalculator(
                characterSchema.CriticalStrike,
                addedCritChance
            ),
            Effects = runeEffects,
            IsPlayer = true,
            PotionEffects = [],
        };
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

        List<ResistanceBoost> extraResistanceBoosts = [];

        if (defender.ShellResistanceBoost is not null)
        {
            extraResistanceBoosts.Add(defender.ShellResistanceBoost);
        }

        if (defender.ProtectiveBubble is not null)
        {
            extraResistanceBoosts.Add(defender.ProtectiveBubble);
        }

        foreach (var resistanceBoost in extraResistanceBoosts)
        {
            resFire += resistanceBoost.ResFire;
            resEarth += resistanceBoost.ResEarth;
            resWater += resistanceBoost.ResWater;
            resAir += resistanceBoost.ResAir;
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

    public static void ProcessParticipantTurn(ProcessParticipantTurnParams participantTurnParams)
    {
        var attacker = participantTurnParams.Attacker;
        var otherAttackers = participantTurnParams.OtherAttackers;
        var defender = participantTurnParams.Defender;
        var otherDefenders = participantTurnParams.OtherDefenders;
        var isBossFight = participantTurnParams.IsBossFight;

        var attackerPotionEffects = participantTurnParams.Attacker.PotionEffects;
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

        HandleProtectiveBubble(attacker, defender, combatLog, turnNumber, individualTurn);

        // Count down vampiric strike cooldown
        if (attacker.VampiricStrikeCooldown > 0)
        {
            attacker.VampiricStrikeCooldown -= 1;
        }

        int poisonDamage = HandleAttackerPoisonDamage(
            attacker,
            defender,
            combatLog,
            turnNumber,
            individualTurn
        );

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

        if (poisonDamage > 0)
        {
            damageToDeal += poisonDamage;
        }

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

    static int HandleAttackerPoisonDamage(
        FightSimParticipant attacker,
        FightSimParticipant defender,
        CombatLog combatLog,
        int turnNumber,
        int individualTurn
    )
    {
        // No reason to loop through effects, if we have already applied poison damage before
        if (defender.ActivePoisonDamage == 0)
        {
            var poison = attacker.Effects.FirstOrDefault(effect => effect.Code == Effect.Poison);

            if (poison is null)
            {
                return 0;
            }

            /**
              The poison effect causes x damage per turn, unless the defender has an antidote. If the defender has an antidote,
              it subtracts the antidote value from the poison, using only 1 antidote.
            **/
            if (poison is not null && turnNumber == 1)
            {
                int poisonDamage = poison.Value;

                foreach (var potion in defender.PotionEffects)
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

                defender.ActivePoisonDamage = poisonDamage;
            }
        }

        if (defender.ActivePoisonDamage > 0)
        {
            combatLog.Log(
                individualTurn,
                attacker.Entity,
                defender.Entity,
                $"[{attacker.Entity.Name}] has poison effect for {defender.ActivePoisonDamage} damage"
            );

            return defender.ActivePoisonDamage;
        }

        return 0;
    }

    static void HandleProtectiveBubble(
        FightSimParticipant attacker,
        FightSimParticipant defender,
        CombatLog combatLog,
        int turnNumber,
        int individualTurn
    )
    {
        var protectiveBubble = defender.Effects.FirstOrDefault(effect =>
            effect.Code == Effect.ProtectiveBubble
        );

        if (protectiveBubble is null)
        {
            return;
        }

        // If it already has been applied this turn, then skip
        if (defender.ProtectiveBubbleChangedOnTurn == turnNumber)
        {
            return;
        }

        defender.ProtectiveBubble = FightSimulator.GetProtectiveBubble(
            defender,
            protectiveBubble.Value
        );

        defender.ProtectiveBubbleChangedOnTurn = turnNumber;

        combatLog.Log(
            individualTurn,
            attacker.Entity,
            defender.Entity,
            $"[{defender.Entity.Name}] got a protective bubble - boost values: Fire = {defender.ProtectiveBubble.ResFire} - Earth = {defender.ProtectiveBubble.ResEarth} - Water = {defender.ProtectiveBubble.ResWater} - Air = {defender.ProtectiveBubble.ResAir}"
        );
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

    public static FightSimParticipant? GetDefenderInFightSimulator(
        FightSimParticipant attacker,
        MonsterType monsterType,
        ICritCalculator switchTargetCalculator,
        List<FightSimParticipant> participants
    )
    {
        bool switchesTarget =
            !attacker.IsPlayer
            && (monsterType == MonsterType.Boss)
            && (monsterType == MonsterType.RaidBoss)
            && switchTargetCalculator.CalculateIsCriticalStrike();

        List<FightSimParticipant> defenderCandidates =
        [
            .. participants.Where(participant =>
                participant.IsPlayer == attacker.IsPlayer || participant.Entity.Hp <= 0
            ),
        ];

        defenderCandidates.Sort(
            (a, b) =>
            {
                if (switchesTarget)
                {
                    // Bosses have an x% chance to switch target to the lowest HP defender
                    return a.Entity.Hp - b.Entity.Hp;
                }
                else
                {
                    // Bosses' default behaviour is to attack the defender with the highest threat
                    return b.Entity.Threat - a.Entity.Threat;
                }
            }
        );

        return defenderCandidates.FirstOrDefault();
    }

    public static int GetAmountToHeal(int healEffect, int entityHp, int entityMaxHp)
    {
        int amountToHeal = Math.Min(healEffect, entityMaxHp - entityHp);

        return amountToHeal;
    }

    public static FightOutcome InnerRunFightSim(
        MonsterType monsterType,
        List<FightSimParticipant> participants,
        string attackingPlayerName
    )
    {
        participants.Sort((a, b) => b.Entity.Initiative.CompareTo(a.Entity.Initiative));

        FightResult? outcome = null;

        int turnNumber = 0;

        int individualTurn = 0;

        CombatLog combatLog = new(
            participants.ElementAt(0).Entity,
            participants.ElementAt(1).Entity
        );

        ICritCalculator bossSwitchTargetCalculator = new DeterministicCritCalculator(
            CHANCE_OF_BOSS_SWITCH_TARGET
        );

        while (outcome is null)
        {
            turnNumber++;

            foreach (var attacker in participants)
            {
                individualTurn++;
                // Elaborate for boss fights, e.g. boss will attack different players
                var defender = GetDefenderInFightSimulator(
                    attacker,
                    monsterType,
                    bossSwitchTargetCalculator,
                    participants
                );

                if (defender is null)
                {
                    outcome = GetFightResult(attacker);

                    combatLog.Log(
                        individualTurn,
                        attacker.Entity,
                        attacker.Entity,
                        $"[{attacker.Entity.Name}] outcome: {outcome.GetDisplayName()} - this scenario should not happen!"
                    );

                    break;
                }

                if (turnNumber > LOSE_AFTER_TURNS)
                {
                    if (monsterType == MonsterType.RaidBoss)
                    {
                        outcome = FightResult.Win;
                        combatLog.Log(
                            individualTurn,
                            attacker.Entity,
                            defender.Entity,
                            $"[{attackingPlayerName}] won! They survived more than {LOSE_AFTER_TURNS} turns fighting a raid boss"
                        );
                    }
                    else
                    {
                        outcome = FightResult.Loss;
                        combatLog.Log(
                            individualTurn,
                            attacker.Entity,
                            defender.Entity,
                            $"[{attackingPlayerName}] lost because they did not win in less than {LOSE_AFTER_TURNS} turns"
                        );
                    }
                }

                if (outcome is null)
                {
                    ProcessParticipantTurn(
                        new ProcessParticipantTurnParams
                        {
                            Attacker = attacker,
                            OtherAttackers = [],
                            Defender = defender,
                            OtherDefenders = [],
                            IsBossFight = false,
                            CombatLog = combatLog,
                            TurnNumber = turnNumber,
                            IndividualTurn = individualTurn,
                        }
                    );

                    bool attackerWon = defender.Entity.Hp <= 0;

                    if (attackerWon)
                    {
                        combatLog.Log(
                            individualTurn,
                            attacker.Entity,
                            defender.Entity,
                            $"[{attacker.Entity.Name}] won."
                        );

                        outcome = GetFightResult(attacker);
                        break;
                    }
                }
            }
        }

        FightSimParticipant? attackingPlayer = null;
        FightSimParticipant? defendingMonster = null;

        List<FightSimParticipant> otherPlayerParticipants = [];

        foreach (var participant in participants)
        {
            if (participant.Entity.Name == attackingPlayerName)
            {
                attackingPlayer = participant;
            }
            else if (participant.IsPlayer)
            {
                otherPlayerParticipants.Add(participant);
            }
            else
            {
                defendingMonster = participant;
            }
        }

        if (attackingPlayer is null || defendingMonster is null)
        {
            throw new AppError(
                $"Could not find attacking player = ${attackingPlayer is null} or defending monster = ${defendingMonster is null}"
            );
        }

        int attackingPlayerPotionsUsed = attackingPlayer.PotionEffects.Sum(potion =>
            potion.OriginalQuantity - potion.Quantity
        );

        int otherAttackingPlayersPotionsUsed = otherPlayerParticipants.Sum(participant =>
        {
            static int selector(FightSimUtility potion) =>
                potion.OriginalQuantity - potion.Quantity;

            return participant.PotionEffects.Sum(selector);
        });

        FightResult fightResult = outcome ?? FightResult.Loss;

        return new FightOutcome
        {
            Result = fightResult, // Should not be necessary
            PlayerHp = Math.Min(attackingPlayer.Entity.Hp, attackingPlayer.OriginalMaxHp), // in case of HP boost pots
            MonsterHp = defendingMonster.Entity.Hp,
            TotalTurns = turnNumber,
            IndvidualTurns = individualTurn,
            ShouldFight = GetShouldFight(
                fightResult,
                attackingPlayer,
                monsterType,
                attackingPlayerPotionsUsed,
                otherAttackingPlayersPotionsUsed,
                otherPlayerParticipants
            ),
            PotionsUsed = attackingPlayerPotionsUsed,
            OtherPlayersPotionsUsed = otherAttackingPlayersPotionsUsed,
            FirstSimCombatLog = combatLog,
            AllPlayerParticipants = [.. otherPlayerParticipants, attackingPlayer],
        };
    }

    static FightResult GetFightResult(FightSimParticipant attacker)
    {
        return attacker.IsPlayer ? FightResult.Win : FightResult.Loss;
    }

    public static bool GetShouldFight(
        FightResult fightResult,
        FightSimParticipant attackingPlayer,
        MonsterType monsterType,
        int attackingPlayerPotionsUsed,
        int otherAttackingPlayersPotionsUsed,
        List<FightSimParticipant> otherPlayerParticipants
    )
    {
        if (fightResult != FightResult.Win)
        {
            return false;
        }
        else if (monsterType == MonsterType.Boss || monsterType == MonsterType.RaidBoss)
        {
            int amountOfPlayersWithEnoughHp = 0;

            int totalPlayers = 1 + otherPlayerParticipants.Count;

            if (PlayerHasEnoughHpAfterFight(attackingPlayer))
            {
                amountOfPlayersWithEnoughHp += 1;
            }

            amountOfPlayersWithEnoughHp += otherPlayerParticipants.Count(
                PlayerHasEnoughHpAfterFight
            );

            bool enoughPlayersLeftWithHp = amountOfPlayersWithEnoughHp >= totalPlayers * 0.5f;

            int maxAllowedPotionsUsed = MAX_AMOUNT_OF_USED_POTIONS * totalPlayers;

            /**
             * For now, we don't want to burn through too many potions to fight bosses,
             * but since we have to survive 100 turns for a raid boss fight, we have to accept
             * that we will burn through a lot of potions.
             */
            bool isBelowMaxPotionsUsed =
                monsterType != MonsterType.Boss
                || attackingPlayerPotionsUsed + otherAttackingPlayersPotionsUsed
                    <= maxAllowedPotionsUsed;

            return enoughPlayersLeftWithHp && isBelowMaxPotionsUsed;
        }

        bool attackerHasEnoughHp = PlayerHasEnoughHpAfterFight(attackingPlayer);

        return attackerHasEnoughHp
            && !otherPlayerParticipants.Exists(participant => participant.Entity.Hp <= 0)
            && attackingPlayerPotionsUsed <= MAX_AMOUNT_OF_USED_POTIONS;
    }

    static bool PlayerHasEnoughHpAfterFight(FightSimParticipant attacker)
    {
        return attacker.Entity.Hp
            >= (attacker.OriginalMaxHp * SHOULD_FIGHT_MAX_HP_PLAYER_THRESHOLD);
    }

    public static List<FightSimResult> SimulateBossFightOutcome(
        PlayerCharacter character,
        List<PlayerCharacter> otherCharacters,
        GameState gameState,
        List<DropSchema> bankItems,
        MonsterSchema monster
    )
    {
        List<PlayerCharacter> allCharacters = [.. otherCharacters, character];

        var currentlyAvailableBankItems = bankItems.ToDictionary(item => item.Code);

        // Look into acquiring potions if needed, we currently only check the bank.
        // var attainablePotions = gameState
        //     .UtilityItemsDict.Select(item => new ItemInInventory
        //     {
        //         Item = item.Value,
        //         Quantity = 100,
        //     })
        //     .ToList();

        List<(FightSimResult Result, CharacterSchema Character)> fightSimResults = [];

        /**
         * First we want to run a sim for each character, to find the best equipment for each to wear.
         * they might not be wearing suitable combat gear at the moment, so we assume that we will probably lose,
         * in these fight results. The idea is to get the best loadout for each character (even if they lose), and then
         * afterward run a fight sim with the best load out for each.
        */
        foreach (var characterForSim in allCharacters)
        {
            var otherCharactersSim = allCharacters
                .Where(character => character.Name != characterForSim.Name)
                .ToList();

            /**
             * A bit dirty, but here we go through the items in the characters inventory,
             * and if there is a match in the bank, we add that quantity to the characters inventory.
            */
            var itemsAvailableToCharacter = characterForSim
                .Schema.Inventory.Select(item =>
                {
                    if (string.IsNullOrWhiteSpace(item.Code))
                    {
                        return null;
                    }

                    int amountInBank =
                        currentlyAvailableBankItems.GetValueOrNull(item.Code)?.Quantity ?? 0;

                    var matchingItem = gameState.ItemsDict[item.Code];

                    return new ItemInInventory
                    {
                        Item = matchingItem,
                        Quantity = item.Quantity + amountInBank,
                    };
                })
                .OfType<ItemInInventory>()
                .ToList();

            /**
             * Now we want to take the items without a match, and add them to the list
            */
            List<ItemInInventory> moreAvailableItems = [];

            foreach (var (key, item) in currentlyAvailableBankItems)
            {
                if (
                    !string.IsNullOrWhiteSpace(item.Code)
                    && !itemsAvailableToCharacter.Exists(itemOnCharacter =>
                        itemOnCharacter.Item.Code == item.Code
                    )
                )
                {
                    moreAvailableItems.Add(
                        new ItemInInventory
                        {
                            Item = gameState.ItemsDict[item.Code],
                            Quantity = item.Quantity,
                        }
                    );
                }
            }

            itemsAvailableToCharacter = [.. itemsAvailableToCharacter.Union(moreAvailableItems)];

            var result = FindBestFightEquipment(
                character,
                gameState,
                monster,
                itemsAvailableToCharacter,
                null,
                otherCharactersSim
            );

            fightSimResults.Add((Result: result.SimResult, Character: characterForSim.Schema));

            var leftOverItemsDict = result.LeftOverItems.ToDictionary(item => item.Item.Code);

            foreach (var (key, item) in currentlyAvailableBankItems)
            {
                var matchInLeftOver = leftOverItemsDict.GetValueOrNull(key);

                if (matchInLeftOver is null)
                {
                    item.Quantity = 0;
                }
                else if (matchInLeftOver.Quantity < item.Quantity)
                {
                    matchInLeftOver.Quantity = item.Quantity;
                }
            }

            currentlyAvailableBankItems = currentlyAvailableBankItems
                .Where(item => item.Value.Quantity > 0)
                .ToDictionary();
        }

        List<CharacterSchema> allCharacterSchemasWithNewItems =
        [
            .. fightSimResults.Select(result =>
            {
                var newSchema = result.Character with { };

                foreach (var item in result.Result.ItemsToEquip)
                {
                    var matchingNewItem = gameState.ItemsDict[item.Code];

                    newSchema = PlayerActionService.SimulateItemEquip(
                        newSchema,
                        null,
                        matchingNewItem,
                        item.Slot.FromSnakeToPascalCase(), // Has to be the actual case of the properties
                        item.Quantity
                    );
                }

                return newSchema;
            }),
        ];

        /**
         * Now we need to run simulation(s) with all of the ideal items for each character, and see what the outcome is.
        */

        // Not needed to calculate all of them, but it's OK for now, might need the code later.
        List<FightSimResult> finalSimResults =
        [
            .. allCharacterSchemasWithNewItems.Select(schema =>
            {
                List<PlayerCharacter> otherCharactersSim =
                [
                    .. allCharacterSchemasWithNewItems
                        .Where(otherSchema => otherSchema.Name != schema.Name)
                        .Select(otherSchema =>
                        {
                            var clonedMatchingCharacter = allCharacters
                                .First(character => character.Schema.Name == otherSchema.Name)
                                .Clone();

                            clonedMatchingCharacter.Schema = otherSchema;

                            return clonedMatchingCharacter;
                        }),
                ];

                var result = FindBestFightEquipment(
                    character,
                    gameState,
                    monster,
                    [],
                    null,
                    otherCharactersSim
                );

                return result.SimResult;
            }),
        ];

        // return finalSimResults.First(result => result.Schema.Name == character.Name);
        return finalSimResults;
    }

    public static FightSimResultWithLeftOverItems FindBestFightEquipment(
        PlayerCharacter character,
        GameState gameState,
        MonsterSchema monster,
        List<ItemInInventory>? originalAllItems = null,
        List<string>? itemTypesToSim = null,
        List<PlayerCharacter>? otherCharacters = null
    )
    {
        var otherCharacterSchemas = (otherCharacters ?? [])
            .Select(character => character.Schema)
            .ToList();

        originalAllItems ??= GetItemsFromInventoryForSim(character.Schema, gameState);

        var allItemsToSim = originalAllItems.Select(item => item).ToList();

        allItemsToSim =
        [
            .. allItemsToSim.Where(item =>
                ItemService.CanUseItem(item.Item, character.Schema, gameState)
            ),
        ];

        allItemsToSim = GetItemsWorthSimming(allItemsToSim);

        // This order should matter somewhat, since e.g. body armor slots typically give more stats than items lower in the list, e.g boots and amulets.
        // List<EquipmentTypeMapping> equipmentTypes = allEquipmentTypes;
        List<EquipmentTypeMapping> tempEquipmentTypes = [];

        if (itemTypesToSim is null)
        {
            tempEquipmentTypes = EquipmentService.AllEquipmentTypes;
        }
        else
        {
            tempEquipmentTypes = EquipmentService
                .AllEquipmentTypes.Where(type => itemTypesToSim.Contains(type.ItemType))
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
            allItemsToSim.Add(
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
            allItemsToSim.Add(
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

        var initialFightOutcome = CalculateFightOutcome(initialSchema, [], monster, gameState);

        var weapons = allItemsToSim
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
                otherCharacterSchemas,
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
                    otherCharacterSchemas,
                    gameState,
                    monster,
                    allItemsToSim,
                    equipmentTypeMapping,
                    bestFightSimResult
                );

                var simResult = result.SimResult;
                allItemsToSim = result.LeftOverItems;

                bestFightSimResult.Schema = simResult.Schema;
                bestFightSimResult.Outcome = simResult.Outcome;

                bestFightSimResult.ItemsToEquip = [.. itemsToEquip.Union(simResult.ItemsToEquip)];
            }

            var potionEffectsToSkip = EffectService.GetPotionEffectsToSkip(
                bestSchemaCandiateWithWeapon,
                monster
            );

            var potionItemsWithoutSkippedEffects = allItemsToSim.FindAll(item =>
                !item.Item.Effects.Exists(effect => potionEffectsToSkip.Contains(effect.Code))
            );

            // Sim potions afterwards
            foreach (var equipmentTypeMapping in potionEquipmentTypes)
            {
                var result = SimItemsForEquipmentType(
                    character,
                    otherCharacterSchemas,
                    gameState,
                    monster,
                    potionItemsWithoutSkippedEffects,
                    equipmentTypeMapping,
                    bestFightSimResult
                );

                var simResult = result.SimResult;
                allItemsToSim = result.LeftOverItems;

                bestFightSimResult.Schema = simResult.Schema;
                bestFightSimResult.Outcome = simResult.Outcome;
                bestFightSimResult.ItemsToEquip = [.. itemsToEquip.Union(simResult.ItemsToEquip)];
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

        var bestCandidate = allCandidates.ElementAt(0);

        var itemsToEquipDict = bestCandidate.ItemsToEquip.ToDictionary(item => item.Code);

        var leftOverItems = originalAllItems
            .Select(item =>
            {
                var matchInItemsToEquip = itemsToEquipDict.GetValueOrNull(item.Item.Code);

                if (matchInItemsToEquip is null)
                {
                    return item;
                }

                return item with
                {
                    Quantity = item.Quantity - matchInItemsToEquip.Quantity,
                };
            })
            .Where(item => item.Quantity > 0)
            .ToList();

        return new FightSimResultWithLeftOverItems
        {
            SimResult = bestCandidate,
            LeftOverItems = leftOverItems,
        };
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

        if (a.AllPlayerParticipants.Count > 0 && b.AllPlayerParticipants.Count > 0)
        {
            int aTotalHp = a.AllPlayerParticipants.Sum(participant => participant.Entity.Hp);

            int bTotalHp = b.AllPlayerParticipants.Sum(participant => participant.Entity.Hp);

            if (aTotalHp - bTotalHp != 0)
            {
                return bTotalHp - aTotalHp;
            }
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
                !EquipmentService.AllEquipmentTypes.Exists(type => type.ItemType == item.Item.Type)
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

    public static FightOutcome CalculateFightOutcome(
        CharacterSchema originalSchema,
        List<CharacterSchema> otherPlayers,
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

            List<FightSimParticipant> allPlayerParticipants =
            [
                .. otherPlayers
                    .Union([originalSchema])
                    .Select(player =>
                        FightSimulator.BuildPlayerFightSimParticipant(
                            player,
                            gameState,
                            playerFullHp,
                            addedCritChance
                        )
                    ),
            ];

            List<FightSimParticipant> participants =
            [
                new FightSimParticipant
                {
                    Entity = monsterClone,
                    OriginalHp = monsterClone.Hp,
                    OriginalMaxHp = monsterClone.MaxHp,
                    CritCalculator = new DeterministicCritCalculator(
                        monsterClone.CriticalStrike,
                        addedCritChance
                    ),
                    Effects = monsterClone.Effects,
                    IsPlayer = false,
                    PotionEffects = [],
                },
                .. allPlayerParticipants,
            ];

            outcomes.Add(
                FightSimulator.InnerRunFightSim(
                    monsterClone.Type,
                    participants,
                    originalSchema.Name
                )
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
            AllPlayerParticipants = [], // TODO: FIX
        };
    }

    public static FightSimResultWithLeftOverItems SimItemsForEquipmentType(
        PlayerCharacter originalCharacter,
        List<CharacterSchema> otherCharacterSchemas,
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
        EquipmentSlot? newItemToEquip = null;

        bestSchemaCandidate.Hp = bestSchemaCandidate.MaxHp;

        int bestItemAmount = 1;

        var equipmentType = equipmentTypeMapping.ItemType;
        var equipmentSlot = equipmentTypeMapping.Slot;
        var items = allItems
            .Where(item => item.Item.Type == equipmentType && item.Quantity > 0)
            .ToList();

        if (items.Count == 0)
        {
            return new FightSimResultWithLeftOverItems
            {
                SimResult = new FightSimResult
                {
                    Schema = bestSchemaCandidate,
                    Outcome = bestFightOutcome,
                    ItemsToEquip = itemsToEquip,
                },
                LeftOverItems = allItems,
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
            ItemSchema? itemSchema =
                gameState.ItemsDict.GetValueOrNull(item.Item.Code)
                ?? throw new Exception(
                    $"Current weapon with code \"{item.Item.Code}\" is null - should never happen"
                );

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

            if (!ItemService.CanUseItem(itemSchema, character.Schema, gameState))
            {
                continue;
            }

            // We cannot have the same effect in both util slots.
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

            var fightOutcome = CalculateFightOutcome(
                characterSchema,
                otherCharacterSchemas,
                monster,
                gameState
            );

            int simOutcome = FightSimulator.CompareSimOutcome(bestFightOutcome, fightOutcome);

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

            Logger.LogDebug(
                $"FindBestFightEquipment: Should swap \"{initialItemCode}\" -> \"{bestItemCandidate.Code}\" in slot \"{snakeCaseSlot}\" for {character.Schema.Name} when fighting \"{monster.Code}\""
            );

            newItemToEquip = new EquipmentSlot
            {
                Code = bestItemCandidate.Code,
                Slot = snakeCaseSlot,
                Quantity = bestItemAmount,
            };

            allItems =
            [
                .. allItems.Select(item =>
                {
                    if (item.Item.Code != newItemToEquip.Code)
                    {
                        return item;
                    }
                    return item with { Quantity = item.Quantity -= newItemToEquip.Quantity };
                }),
            ];
        }

        return new FightSimResultWithLeftOverItems
        {
            SimResult = new FightSimResult
            {
                Schema = bestSchemaCandidate,
                Outcome = bestFightOutcome,
                ItemsToEquip = newItemToEquip is null ? [] : [newItemToEquip],
            },
            LeftOverItems = allItems,
        };
    }
}

public record FightOutcome
{
    public FightResult Result { get; init; }

    public MonsterType MonsterType { get; set; } = MonsterType.Normal;

    public int PlayerHp { get; init; }

    public int MonsterHp { get; init; }

    public int TotalTurns { get; init; }
    public int IndvidualTurns { get; init; }

    public bool ShouldFight { get; init; }

    public int PotionsUsed { get; set; } = 0;
    public int OtherPlayersPotionsUsed { get; set; } = 0;

    public required CombatLog FirstSimCombatLog { get; set; }

    public required List<FightSimParticipant> AllPlayerParticipants { get; set; } = [];
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

public record FightSimResultWithLeftOverItems
{
    public required FightSimResult SimResult { get; set; }
    public required List<ItemInInventory> LeftOverItems { get; set; } = [];
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
    public required List<FightSimUtility> PotionEffects = [];

    public required int OriginalHp { get; set; }
    public required int OriginalMaxHp { get; set; }

    public int Barrier { get; set; } = 0;
    public int Frenzy { get; set; } = 0;
    public bool ShellHasBeenActivated { get; set; } = false;
    public int ShellTurnsRemaining { get; set; } = 0;
    public ResistanceBoost? ShellResistanceBoost { get; set; }

    public int ProtectiveBubbleChangedOnTurn { get; set; } = 0;
    public ResistanceBoost? ProtectiveBubble { get; set; }

    public int VampiricStrikeCooldown { get; set; } = 0;

    public int ActivePoisonDamage { get; set; } = 0;

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
    public required CombatLog CombatLog { get; set; }
    public required int TurnNumber { get; set; }
    public required int IndividualTurn { get; set; }
}

public record ResistanceBoost
{
    public required int ResFire { get; set; } = 0;

    public required int ResEarth { get; set; } = 0;

    public required int ResWater { get; set; } = 0;

    public required int ResAir { get; set; } = 0;
}
