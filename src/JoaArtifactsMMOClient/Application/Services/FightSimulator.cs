using System.Linq.Expressions;
using Application;
using Application.ArtifactsApi.Schemas;
using Application.Character;
using Application.Errors;
using Application.Jobs;
using Application.Records;
using Application.Services;
using OneOf.Types;

namespace Applicaton.Services.FightSimulator;

public class FightSimulator
{
    private static readonly double CRIT_DAMAGE_MODIFIER = 0.5;
    private static readonly int MAX_LEVEL = 50;
    private static readonly double PERCENTAGE_OF_SIMS_TO_WIN = 0.85;

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

            while (outcome is null)
            {
                turnNumber++;

                foreach (var attacker in participants)
                {
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
                            }
                        }
                    }
                    // Elaborate for boss fights, e.g. boss will attack different players
                    var defender = participants.FirstOrDefault(participant =>
                        participant.isPlayer != attacker.isPlayer
                    );

                    ProcessParticipantTurn(
                        attacker.entity,
                        attacker.critCalculator,
                        attacker.effects,
                        potionEffectsForTurn,
                        defender.entity,
                        turnNumber
                    );

                    defender.entity.Hp -= poisonDamage;

                    bool attackerWon = defender.entity.Hp <= 0;

                    if (attackerWon)
                    {
                        if (attacker.isPlayer)
                        {
                            outcome = FightResult.Win;
                        }
                        else
                        {
                            outcome = FightResult.Loss;
                        }

                        remainingMonsterHp = participants
                            .FirstOrDefault(participant => !participant.isPlayer)
                            .entity.Hp;

                        remainingPlayerHp = participants
                            .FirstOrDefault(participant => participant.isPlayer)
                            .entity.Hp;

                        break;
                    }
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
                    ShouldFight =
                        outcome == FightResult.Win
                        && remainingPlayerHp >= (characterSchema.MaxHp * 0.35),
                    PotionsUsed = potionsUsedInSim,
                }
            );
        }

        int amountWon = 0;
        int amountShouldFight = 0;

        int playerHp = 0;
        int monsterHp = 0;
        int totalTurns = 0;
        int potionsUsed = 0;

        int fightSimulations = outcomes.Count;

        foreach (var outcome in outcomes)
        {
            amountWon += outcome.Result == FightResult.Win ? 1 : 0;
            amountShouldFight += outcome.ShouldFight == true ? 1 : 0;
            playerHp += outcome.PlayerHp;
            monsterHp += outcome.MonsterHp;
            totalTurns += outcome.TotalTurns;
            potionsUsed += outcome.PotionsUsed;
        }

        playerHp = (int)Math.Floor((double)playerHp / fightSimulations);
        monsterHp = (int)Math.Floor((double)monsterHp / fightSimulations);
        totalTurns = (int)Math.Floor((double)totalTurns / fightSimulations);
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
            PotionsUsed = potionsUsed,
        };
    }

    private static (int damage, bool wasCrit) CalculateTurnDamage(
        FightEntity attacker,
        ICritCalculator attackerCritCalculator,
        FightEntity defender
    )
    {
        bool isCrit = false;

        if (attackerCritCalculator.CalculateIsCriticalStrike())
        // if (new Random().NextDouble() <= attacker.CriticalStrike)
        {
            isCrit = true;
        }

        var fireDamage = CalculateElementalAttack(
            attacker.AttackFire,
            attacker.DmgFire,
            attacker.Dmg,
            isCrit,
            defender.ResFire
        );

        var earthDamage = CalculateElementalAttack(
            attacker.AttackEarth,
            attacker.DmgEarth,
            attacker.Dmg,
            isCrit,
            defender.ResEarth
        );
        var waterDamage = CalculateElementalAttack(
            attacker.AttackWater,
            attacker.DmgWater,
            attacker.Dmg,
            isCrit,
            defender.ResWater
        );
        var airDamage = CalculateElementalAttack(
            attacker.AttackAir,
            attacker.DmgAir,
            attacker.Dmg,
            isCrit,
            defender.ResAir
        );

        return (fireDamage + earthDamage + waterDamage + airDamage, isCrit);
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
        List<SimpleEffectSchema> attackerRuneEffects,
        List<FightSimUtility> attackerPotionEffects,
        FightEntity defender,
        int totalTurns
    )
    {
        var attackerDamage = CalculateTurnDamage(attacker, critCalculator, defender);

        var damageWithEffects = attackerDamage.damage;

        // Amplify damage if needed, e.g. burn rune. Figure out how we handle poisons, because technically it might be easier
        // to handle those outside of this function, because the poison damage can be mitigated
        if (totalTurns <= 2)
        {
            SimpleEffectSchema? burn = attackerRuneEffects.FirstOrDefault(effect =>
                effect.Code == Effect.Burn
            );

            if (burn is not null)
            {
                // Decrease burn damage by 10% each turn. So if burn value is 20%, then it's 20, 10, 0.
                int multiplicationFactor = Math.Max(burn.Value - (totalTurns - 1), 0);

                // int initialDmg = burn.Value - multiplicationFactor;

                // double burnFactor = initialDmg * 0.01;
                double burnFactor = multiplicationFactor * 0.01;

                int burnDamage =
                    burnFactor > 0 ? (int)Math.Round(damageWithEffects * burnFactor) : 0;

                damageWithEffects += burnDamage;
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
                    attacker.Hp += restoreEffect.Value;

                    attacker.Hp = Math.Min(attacker.Hp, attacker.MaxHp);

                    effect.Quantity--;
                }
            }
        }

        defender.Hp -= attackerDamage.damage;

        if (defender.Hp <= 0)
        {
            return;
        }

        if (attackerDamage.wasCrit)
        {
            SimpleEffectSchema? lifesteal = attackerRuneEffects.FirstOrDefault(effect =>
                effect.Code == Effect.Lifesteal
            );

            if (lifesteal is not null)
            {
                // We use the raw damage here, don't think lifesteal works with burn
                int heal = (int)Math.Round(attackerDamage.damage * lifesteal.Value * 0.01);

                attacker.Hp += heal;
            }
        }

        if (totalTurns % 3 == 0)
        {
            SimpleEffectSchema? heal = attackerRuneEffects.FirstOrDefault(effect =>
                effect.Code == Effect.Healing
            );

            if (heal is not null)
            {
                // We use the raw damage here, don't think lifesteal works with burn
                int amountToHeal = (int)Math.Round(attacker.MaxHp * (heal.Value * 0.01));

                attacker.Hp += amountToHeal;
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

        string initialWeaponCode = initialSchema.WeaponSlot;

        var initialFightOutcome = CalculateFightOutcome(initialSchema, monster, gameState);

        var weapons = allItems
            .Where(item => item.Item.Type == "weapon" && item.Item.Subtype != "tool")
            .ToList();

        if (!string.IsNullOrWhiteSpace(initialSchema.WeaponSlot))
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
                    /**
                        This is kinda hacky, but we do this because we want the second time running,
                        to know which potion we put in Util1
                    **/
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

        var bestSchemaCandiate = originalResult.Schema with
        { };
        var bestFightOutcome = originalResult.Outcome with { };
        var itemsToEquip = originalResult.ItemsToEquip.Select(item => item).ToList();

        bestSchemaCandiate.Hp = bestSchemaCandiate.MaxHp;

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
                Schema = bestSchemaCandiate,
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
                        ? bestSchemaCandiate.Utility2Slot
                        : bestSchemaCandiate.Utility1Slot
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

            var characterSchema = bestSchemaCandiate with { };

            characterSchema = PlayerActionService.SimulateItemEquip(
                characterSchema,
                bestItemCandidate,
                itemSchema,
                equipmentSlot,
                item.Quantity
            );

            var fightOutcome = CalculateFightOutcome(characterSchema, monster, gameState);

            bool fightOutcomeIsBetter = CompareSimOutcome(bestFightOutcome, fightOutcome) == 1;

            if (fightOutcomeIsBetter)
            {
                /**
                 * If we are not simming potions, then we want to do simulations without potions,
                 * because restore HP pots can skew the outcome, e.g. a worse item setup might have
                 * the character HP pots earlier, which can end up with them having a higher amount of
                 * remaining HP, but they also used more potions.
                 *
                **/
                if (equipmentTypeMapping.ItemType != "utility")
                {
                    if (fightOutcome.PotionsUsed > bestFightOutcome.PotionsUsed)
                    {
                        continue;
                    }
                }

                bestFightOutcome = fightOutcome;
                bestItemCandidate = item.Item;
                bestSchemaCandiate = characterSchema;
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
            Schema = bestSchemaCandiate,
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
            if (a.TotalTurns < b.TotalTurns)
            {
                return aWinsValue;
            }

            if (a.TotalTurns > b.TotalTurns)
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

    public static List<MonsterSchema> GetRelevantMonstersForCharacter(PlayerCharacter character)
    {
        int maxToFindPerCategory = 3;

        int playerLevel = character.Schema.Level;

        List<MonsterSchema> relevantMonsters = [];

        List<MonsterSchema> mediumMonsters = [];

        List<MonsterSchema> toughMonsters = [];

        int lowerLevelBound = playerLevel - 5;

        if (lowerLevelBound < 0)
        {
            lowerLevelBound = 1;
        }

        int upperLevelBound = playerLevel + 2;

        if (upperLevelBound > MAX_LEVEL)
        {
            upperLevelBound = MAX_LEVEL;
        }

        // Find the most difficult monsters that the character should realistically be able to fight,
        // at their current level. It's okay that the character cannot defeat them all at the moment.

        foreach (var monster in relevantMonsters)
        {
            if (monster.Type == MonsterType.Boss)
            {
                continue;
            }
            if (
                mediumMonsters.Count >= maxToFindPerCategory
                && toughMonsters.Count >= maxToFindPerCategory
            )
            {
                break;
            }
            if (monster.Level < lowerLevelBound || monster.Level > upperLevelBound)
            {
                continue;
            }

            if (monster.Level >= playerLevel + 2 && toughMonsters.Count < maxToFindPerCategory)
            {
                toughMonsters.Add(monster);
            }
            else if (
                monster.Level <= playerLevel
                && monster.Level + 3 >= playerLevel
                && mediumMonsters.Count < maxToFindPerCategory
            )
            {
                toughMonsters.Add(monster);
            }
        }

        return relevantMonsters;
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
                        // They don't overlap and count as upgrades
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

    public static async Task<List<CharacterJob>?> GetJobsToFightMonster(
        PlayerCharacter character,
        GameState gameState,
        MonsterSchema monster
    )
    {
        var jobsToGetItems = await character.PlayerActionService.GetJobsToGetItemsToFightMonster(
            character,
            gameState,
            monster
        );

        // Return null if they shouldn't fight, return list of jobs if they should, return empty list if they have optimal items
        if (
            jobsToGetItems is null
            || jobsToGetItems.Count == 0
                && !FindBestFightEquipment(character, gameState, monster).Outcome.ShouldFight
        )
        {
            return null;
        }

        var bankItems = await gameState.BankItemCache.GetBankItems(character);

        // We assume that items that are lower level, are also easier to get (mobs less difficult to fight).
        // The issue can be that our character might only barely be able to fight the monster, so rather get the easier items first
        jobsToGetItems.Sort(
            (a, b) =>
            {
                if (bankItems.Data.Exists(item => item.Code == a.Code && item.Quantity >= a.Amount))
                {
                    return -1;
                }
                else if (
                    bankItems.Data.Exists(item => item.Code == b.Code && item.Quantity >= b.Amount)
                )
                {
                    return 1;
                }
                // If we can buy an item straight away, then let us do that first
                var aMatchingNpcItem = gameState.NpcItemsDict.ContainsKey(a.Code);

                var bMatchingNpcItem = gameState.NpcItemsDict.ContainsKey(b.Code);

                if (aMatchingNpcItem && !bMatchingNpcItem)
                {
                    return -1;
                }
                else if (!aMatchingNpcItem && bMatchingNpcItem)
                {
                    return 1;
                }

                var aLevel = gameState.ItemsDict.GetValueOrNull(a.Code)!.Level;
                var bLevel = gameState.ItemsDict.GetValueOrNull(b.Code)!.Level;

                return aLevel.CompareTo(bLevel);
            }
        );
        return jobsToGetItems;
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
}

public record FightOutcome
{
    public FightResult Result { get; init; }

    public int PlayerHp { get; init; }

    public int MonsterHp { get; init; }

    public int TotalTurns { get; init; }

    public bool ShouldFight { get; init; }

    public int PotionsUsed { get; set; } = 0;
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
