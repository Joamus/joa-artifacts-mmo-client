using System.Collections.Immutable;
using Application;
using Application.ArtifactsApi.Schemas;
using Application.Character;
using Application.Records;
using Application.Services;
using Application.Services.Combat;

namespace Applicaton.Services.FightSimulator;

public class FightSimulator
{
    private static readonly double CRIT_DAMAGE_MODIFIER = 0.5;
    private static readonly int MAX_LEVEL = 50;
    private static readonly double PERCENTAGE_OF_SIMS_TO_WIN = 0.85;

    private static readonly int MAX_AMOUNT_OF_USED_POTIONS = 10;
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

            List<(
                FightEntity entity,
                ICritCalculator critCalculator,
                List<SimpleEffectSchema> effects,
                bool isPlayer
            )> participants =
            [
                (
                    monsterClone,
                    new DeterministicCritCalculator(monsterClone.CriticalStrike, addedCritChance),
                    monsterClone.Effects,
                    false
                ),
                (
                    characterSchema,
                    new DeterministicCritCalculator(
                        characterSchema.CriticalStrike,
                        addedCritChance
                    ),
                    runeEffects,
                    true
                ),
            ];

            participants.Sort((a, b) => b.entity.Initiative.CompareTo(a.entity.Initiative));
            var remainingPlayerHp = playerFullHp ? characterSchema.MaxHp : characterSchema.Hp;
            var remainingMonsterHp = initMonsterHp;

            FightResult? outcome = null;

            int turnNumber = 0;

            int individualTurn = 0;

            CombatLog combatLog = new CombatLog(
                participants.ElementAt(0).entity,
                participants.ElementAt(1).entity
            );

            while (outcome is null)
            {
                turnNumber++;

                foreach (var attacker in participants)
                {
                    individualTurn++;
                    // Elaborate for boss fights, e.g. boss will attack different players
                    var defender = participants.FirstOrDefault(participant =>
                        participant.isPlayer != attacker.isPlayer
                    );

                    List<FightSimUtility> potionEffectsForTurn = attacker.isPlayer ? potions : [];

                    int poisonDamage = 0;
                    var poison = attacker.effects.FirstOrDefault(effect =>
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
                            attacker.entity,
                            defender.entity,
                            $"[{attacker.entity.Name}] has poison effect for {poisonDamage} damage"
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
                                    attacker.entity,
                                    defender.entity,
                                    $"[{attacker.entity.Name}] has their poison effect mitigated with {antidote.Value} points of antidote - damage is {poisonDamage}"
                                );
                            }
                        }
                    }

                    defender.entity.Hp -= poisonDamage;

                    if (poisonDamage > 0)
                    {
                        combatLog.Log(
                            individualTurn,
                            attacker.entity,
                            defender.entity,
                            $"[{attacker.entity.Name}] deals {poisonDamage} poison damage"
                        );
                    }

                    ProcessParticipantTurn(
                        attacker.entity,
                        attacker.critCalculator,
                        combatLog,
                        attacker.effects,
                        potionEffectsForTurn,
                        defender.entity,
                        turnNumber,
                        individualTurn
                    );

                    bool attackerWon = defender.entity.Hp <= 0;

                    if (attackerWon)
                    {
                        combatLog.Log(
                            individualTurn,
                            attacker.entity,
                            defender.entity,
                            $"[{attacker.entity.Name}] won."
                        );

                        if (attacker.isPlayer)
                        {
                            outcome = FightResult.Win;
                            remainingPlayerHp = attacker.entity.Hp;
                            remainingMonsterHp = defender.entity.Hp;
                        }
                        else
                        {
                            outcome = FightResult.Loss;
                            remainingMonsterHp = attacker.entity.Hp;
                            remainingPlayerHp = defender.entity.Hp;
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
        FightEntity attacker,
        ICritCalculator attackerCritCalculator,
        FightEntity defender
    )
    {
        TurnDamageResult result = new TurnDamageResult
        {
            ElementalAttacks = [],
            TotalDamage = 0,
            IsCrit = false,
        };

        if (attackerCritCalculator.CalculateIsCriticalStrike())
        {
            result.IsCrit = true;
        }

        var fireDamage = CalculateElementalAttack(
            attacker.AttackFire,
            attacker.DmgFire,
            attacker.Dmg,
            result.IsCrit,
            defender.ResFire
        );

        if (fireDamage > 0)
        {
            result.ElementalAttacks.Add((fireDamage, "fire"));
        }

        var earthDamage = CalculateElementalAttack(
            attacker.AttackEarth,
            attacker.DmgEarth,
            attacker.Dmg,
            result.IsCrit,
            defender.ResEarth
        );

        if (earthDamage > 0)
        {
            result.ElementalAttacks.Add((earthDamage, "earth"));
        }

        var waterDamage = CalculateElementalAttack(
            attacker.AttackWater,
            attacker.DmgWater,
            attacker.Dmg,
            result.IsCrit,
            defender.ResWater
        );

        if (waterDamage > 0)
        {
            result.ElementalAttacks.Add((waterDamage, "air"));
        }

        var airDamage = CalculateElementalAttack(
            attacker.AttackAir,
            attacker.DmgAir,
            attacker.Dmg,
            result.IsCrit,
            defender.ResAir
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

    public static void ProcessParticipantTurn(
        FightEntity attacker,
        ICritCalculator critCalculator,
        CombatLog combatLog,
        List<SimpleEffectSchema> attackerRuneEffects,
        List<FightSimUtility> attackerPotionEffects,
        FightEntity defender,
        int turnNumber,
        int individualTurn
    )
    {
        var attack = CalculateTurnDamage(attacker, critCalculator, defender);

        var damageWithEffects = attack.TotalDamage;

        // Amplify damage if needed, e.g. burn rune. Figure out how we handle poisons, because technically it might be easier
        // to handle those outside of this function, because the poison damage can be mitigated
        if (turnNumber <= 2)
        {
            SimpleEffectSchema? burn = attackerRuneEffects.FirstOrDefault(effect =>
                effect.Code == Effect.Burn
            );

            if (burn is not null)
            {
                // Decrease burn damage by 10% each turn. So if burn value is 20%, then it's 20, 10, 0.
                int multiplicationFactor = Math.Max(burn.Value - (turnNumber - 1), 0);

                // int initialDmg = burn.Value - multiplicationFactor;

                // double burnFactor = initialDmg * 0.01;
                double burnFactor = multiplicationFactor * 0.01;

                int burnDamage =
                    burnFactor > 0 ? (int)Math.Round(damageWithEffects * burnFactor) : 0;

                damageWithEffects += burnDamage;

                combatLog.Log(
                    individualTurn,
                    attacker,
                    defender,
                    $"[{attacker.Name}] deals {burnDamage} burn damage to {defender.Name}"
                );
            }
        }

        foreach (var effect in attackerPotionEffects)
        {
            if (effect.Quantity < 0)
            {
                continue;
            }

            var restoreEffect = effect.Item.Effects.FirstOrDefault(effect =>
                effect.Code == Effect.Restore
            );

            if (restoreEffect is not null)
            {
                if (attacker.Hp <= attacker.MaxHp / 2)
                {
                    int amountHealed = GetAmountToHeal(
                        restoreEffect.Value,
                        attacker.Hp,
                        attacker.MaxHp
                    );

                    attacker.Hp += amountHealed;

                    amountHealed = effect.Quantity--;

                    combatLog.Log(
                        individualTurn,
                        attacker,
                        defender,
                        $"[{attacker.Name}] heals {amountHealed} from a health potion"
                    );
                }
            }
        }

        foreach (var elementalAttack in attack.ElementalAttacks)
        {
            defender.Hp -= elementalAttack.Damage;

            combatLog.Log(
                individualTurn,
                attacker,
                defender,
                $"[{attacker.Name}] used {elementalAttack.Elemental} attack and dealt {elementalAttack.Damage} damage"
            );
        }

        if (defender.Hp <= 0)
        {
            return;
        }

        if (attack.IsCrit)
        {
            SimpleEffectSchema? lifesteal = attackerRuneEffects.FirstOrDefault(effect =>
                effect.Code == Effect.Lifesteal
            );

            if (lifesteal is not null)
            {
                // We use the raw damage here, don't think lifesteal works with burn
                int heal = (int)Math.Round(attack.TotalDamage * lifesteal.Value * 0.01);

                heal = GetAmountToHeal(heal, attacker.Hp, attacker.MaxHp);

                attacker.Hp += heal;

                combatLog.Log(
                    individualTurn,
                    attacker,
                    defender,
                    $"[{attacker.Name}] heals {heal} from Life steal effect"
                );
            }
        }

        if (turnNumber % 3 == 0)
        {
            SimpleEffectSchema? heal = attackerRuneEffects.FirstOrDefault(effect =>
                effect.Code == Effect.Healing
            );

            if (heal is not null)
            {
                // We use the raw damage here, don't think lifesteal works with burn
                int amountToHeal = (int)Math.Round(attacker.MaxHp * (heal.Value * 0.01));

                amountToHeal = GetAmountToHeal(amountToHeal, attacker.Hp, attacker.MaxHp);
                attacker.Hp += amountToHeal;

                combatLog.Log(
                    individualTurn,
                    attacker,
                    defender,
                    $"[{attacker.Name}] heals {amountToHeal} from Healing effect"
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

                bestSchemaCandiateWithWeapon = result.Schema;
                bestFightOutcomeWithWeapon = result.Outcome;
                itemsToEquip = itemsToEquip.Union(result.ItemsToEquip).ToList();

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
                    new FightSimResult
                    {
                        Schema = bestSchemaCandiateWithWeapon,
                        Outcome = bestFightOutcomeWithWeapon,
                        ItemsToEquip = itemsToEquip,
                    }
                );

                bestSchemaCandiateWithWeapon = result.Schema;
                bestFightOutcomeWithWeapon = result.Outcome;
                itemsToEquip = itemsToEquip.Union(result.ItemsToEquip).ToList();
            }

            allCandidates.Add(
                new FightSimResult
                {
                    Schema = bestSchemaCandiateWithWeapon,
                    Outcome = bestFightOutcomeWithWeapon,
                    ItemsToEquip = itemsToEquip,
                }
            );
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
        PlayerCharacter character,
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

        // TODO: Loop through all weapons, and find the best combination with each weapon.
        // We can maybe skip tools, and only take one if we literally have no other weapons

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

        EquipmentSlot? equippedItem = null;

        equippedItem = character.GetEquipmentSlot(equipmentSlot);

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
        int maxToFindPerCategory = 5;

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

        return filteredMonsters.GetRange(0, 5);
    }

    public static HashSet<string> GetItemsRelevantMonsters(
        PlayerCharacter character,
        GameState gameState,
        List<ItemInInventory> items
    )
    {
        HashSet<string> relevantItems = [];

        var relevantMonsters = GetRelevantMonstersForCharacter(character, gameState);

        foreach (var monster in relevantMonsters)
        {
            var bestFightItems = (
                FindBestFightEquipment(
                    character,
                    gameState,
                    monster,
                    character
                        .Schema.Inventory.Where(item => !string.IsNullOrEmpty(item.Code))
                        .Select(item => new ItemInInventory
                        {
                            Item = gameState.ItemsDict[item.Code],
                            Quantity = item.Quantity,
                        })
                        .Union(items)
                        .ToList()
                )
            ).ItemsToEquip;

            foreach (var item in bestFightItems)
            {
                // if (!relevantItems.Contains(item.Code))
                // {
                relevantItems.Add(item.Code);
                // }
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

record AttackResult
{
    // public string Element { get; set; } = "";

    public int Damage { get; set; }

    public bool WasCrit { get; set; }
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
