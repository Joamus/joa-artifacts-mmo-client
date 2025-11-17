using System.Linq.Expressions;
using Application;
using Application.ArtifactsApi.Schemas;
using Application.Character;
using Application.Errors;
using Application.Records;
using Application.Services;

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
        };

    // We assume that monsters will crit more often than us, just to ensure that we don't take on fights too often, that we will probably not win.
    // private static readonly double MONSTER_CRIT_BIAS = 1.25;

    private static ILogger<FightSimulator> logger = LoggerFactory
        .Create(AppLogger.options)
        .CreateLogger<FightSimulator>();

    public static FightOutcome CalculateFightOutcome(
        CharacterSchema originalSchema,
        MonsterSchema monster,
        GameState gameState,
        int iterations = 10,
        bool playerFullHp = true
    )
    {
        List<FightOutcome> outcomes = [];

        for (int i = 0; i < iterations; i++)
        {
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
                foreach (var effect in runeEffects)
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
                List<SimpleEffectSchema> effects,
                bool isPlayer
            )> participants =
            [
                (monsterClone, monsterClone.Effects, false),
                (characterSchema, runeEffects, true),
            ];

            participants.Sort((a, b) => b.entity.Initiative.CompareTo(a.entity.Initiative));
            var remainingPlayerHp = playerFullHp ? characterSchema.MaxHp : characterSchema.Hp;
            var remainingMonsterHp = initMonsterHp;

            FightResult? outcome = null;

            int turnNumber = 1;

            while (outcome is null)
            {
                foreach (var attacker in participants)
                {
                    List<FightSimUtility> potionEffectsForTurn = attacker.isPlayer ? potions : [];

                    int poisonDamage = 0;

                    /**
                      The poison effect causes x damage per turn, unless the defender has an antidote. If the defender has an antidote,
                      it subtracts the antidote value from the poison, using only 1 antidote.
                    **/
                    if (turnNumber == 1)
                    {
                        var poison = attacker.effects.FirstOrDefault(effect =>
                            effect.Code == Effect.Poison
                        );

                        if (poison is not null)
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

                                    if (poisonDamage < 0)
                                    {
                                        poisonDamage = 0;
                                    }
                                    potion.Quantity--;
                                    break;
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
                        runeEffects,
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
                turnNumber++;
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
                        && remainingPlayerHp >= (characterSchema.MaxHp * 0.30),
                }
            );
        }

        int amountWon = 0;
        int amountShouldFight = 0;

        int playerHp = 0;
        int monsterHp = 0;
        int totalTurns = 0;

        int fightSimulations = outcomes.Count;

        foreach (var outcome in outcomes)
        {
            amountWon += outcome.Result == FightResult.Win ? 1 : 0;
            amountShouldFight += outcome.ShouldFight == true ? 1 : 0;
            playerHp += outcome.PlayerHp;
            monsterHp += outcome.MonsterHp;
            totalTurns += outcome.TotalTurns;
        }

        playerHp = (int)Math.Floor((double)playerHp / fightSimulations);
        monsterHp = (int)Math.Floor((double)monsterHp / fightSimulations);
        totalTurns = (int)Math.Floor((double)totalTurns / fightSimulations);

        FightResult generallyWon =
            (amountWon / fightSimulations) > PERCENTAGE_OF_SIMS_TO_WIN
                ? FightResult.Win
                : FightResult.Loss;

        return new FightOutcome
        {
            Result = generallyWon,
            ShouldFight = generallyWon == FightResult.Win,
            PlayerHp = playerHp,
            MonsterHp = monsterHp,
            TotalTurns = totalTurns,
        };
    }

    public static FightSimResult GetFightSimWithBestEquipment(
        PlayerCharacter character,
        MonsterSchema monster,
        GameState gameState
    )
    {
        var result = FindBestFightEquipment(character, gameState, monster);

        return result;
    }

    private static (int damage, bool wasCrit) CalculateTurnDamage(
        FightEntity attacker,
        FightEntity defender
    )
    {
        bool isCrit = false;

        if (new Random().NextDouble() <= attacker.CriticalStrike)
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
            Math.Round(baseDamage + baseDamage * (elementalMultiplier + damageMultiplier) * 0.01); // Not sure where the 0.01 is from

        if (isCrit)
        {
            damage *= (int)(1 + CRIT_DAMAGE_MODIFIER);
        }

        damage = (int)Math.Round(damage / (1 + resistance * 0.01));

        return damage;
    }

    public static void ProcessParticipantTurn(
        FightEntity attacker,
        List<SimpleEffectSchema> attackerRuneEffects,
        List<FightSimUtility> attackerPotionEffects,
        FightEntity defender,
        int totalTurns
    )
    {
        var attackerDamage = CalculateTurnDamage(attacker, defender);

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
                burn.Value -= (totalTurns + 1) - 1;
                int turnMultiplier = 2 - (totalTurns - 1);
                // Decrease burn damage by 10% each turn. So if burn value is 20%, then it's 20, 10, 0.
                double burnFactor = Math.Max((burn.Value * 0.01) - (turnMultiplier * 0.1), 0);

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

            if (
                restoreEffect is not null
                && attacker is CharacterSchema // Should always be true
            )
            {
                var attackerAsPlayer = (CharacterSchema)attacker;

                if (attackerAsPlayer.Hp <= attackerAsPlayer.MaxHp / 2)
                {
                    attackerAsPlayer.Hp += restoreEffect.Value;

                    attackerAsPlayer.Hp = Math.Min(attackerAsPlayer.Hp, attackerAsPlayer.MaxHp);

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
                effect.Code == Effect.Burn
            );

            if (lifesteal is not null)
            {
                // We use the raw damage here, don't think lifesteal works with burn
                int heal = (int)Math.Round(attackerDamage.damage * lifesteal.Value * 0.01);

                attacker.Hp += heal;
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
            allItems = character
                .Schema.Inventory.Where(item => !string.IsNullOrEmpty(item.Code))
                .Select(item => new ItemInInventory
                {
                    Item = gameState.ItemsDict[item.Code],
                    Quantity = item.Quantity,
                })
                .ToList();
        }
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
                // bestFightSimResult with
                // { }
                );

                bestSchemaCandiateWithWeapon = result.Schema;
                bestFightOutcomeWithWeapon = result.Outcome;
                itemsToEquip = itemsToEquip.Union(result.ItemsToEquip).ToList();

                // bestFightSimResult = result;
                // bestFightSimResult.ItemsToEquip = itemsToEquip;
            }

            // Sim potions afterwards
            foreach (var equipmentTypeMapping in potionEquipmentTypes)
            {
                var result = SimItemsForEquipmentType(
                    character,
                    gameState,
                    monster,
                    allItems,
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
                // bestFightSimResult
                // bestFightSimResult with
                // { }
                );

                bestSchemaCandiateWithWeapon = result.Schema;
                bestFightOutcomeWithWeapon = result.Outcome;
                itemsToEquip = itemsToEquip.Union(result.ItemsToEquip).ToList();

                // bestFightSimResult = result;
                // bestFightSimResult.ItemsToEquip = itemsToEquip;

                // bestSchemaCandiateWithWeapon = result.Schema;
                // bestFightOutcomeWithWeapon = result.Outcome;
                // itemsToEquip = itemsToEquip.Union(result.ItemsToEquip).ToList();
            }

            allCandidates.Add(
                new FightSimResult
                {
                    Schema = bestSchemaCandiateWithWeapon,
                    Outcome = bestFightOutcomeWithWeapon,
                    ItemsToEquip = itemsToEquip,
                }
            // bestFightSimResult
            );
        }

        allCandidates.Sort((a, b) => CompareSimOutcome(a.Outcome, b.Outcome));

        return allCandidates.ElementAt(0);
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
        var items = allItems.Where(item => item.Item.Type == equipmentType).ToList();

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
                1
            );

            var fightOutcome = CalculateFightOutcome(characterSchema, monster, gameState);

            bool fightOutcomeIsBetter = CompareSimOutcome(bestFightOutcome, fightOutcome) == 1;

            // if (
            //     bestItemCandidate is null
            //     || fightOutcome.Result == FightResult.Win
            //         && (
            //             bestFightOutcome is null
            //             || bestFightOutcome.Result != FightResult.Win
            //             || (
            //                 fightOutcome.PlayerHp > bestFightOutcome.PlayerHp
            //                 || bestFightOutcome.TotalTurns < fightOutcome.TotalTurns
            //             )
            //         )
            // )
            if (fightOutcomeIsBetter)
            {
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
        if (
            a.Result == FightResult.Win
            && (
                b.Result != FightResult.Win
                || (
                    a.PlayerHp > b.PlayerHp
                    || (a.TotalTurns < b.TotalTurns && a.PlayerHp >= b.PlayerHp)
                )
            )
        )
        {
            return -1;
        }
        else
        {
            return 1;
        }
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
}

public record FightOutcome
{
    public FightResult Result { get; init; }

    public int PlayerHp { get; init; }

    public int MonsterHp { get; init; }

    public int TotalTurns { get; init; }

    public bool ShouldFight { get; init; }
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
