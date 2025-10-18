using System.Security.Principal;
using Application;
using Application.ArtifactsApi.Schemas;
using Application.Character;
using Application.Errors;
using Application.Services;
using OneOf;
using OneOf.Types;

namespace Applicaton.Services.FightSimulator;

public class FightSimulator
{
    private static readonly double CRIT_DAMAGE_MODIFIER = 0.5;

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
        CharacterSchema characterSchema = originalSchema with { };

        List<FightOutcome> outcomes = [];

        List<FightSimUtility> potions = [];

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

        ApplyPreFightEffects(characterSchema, gameState, potions);

        // Add runes to this
        List<SimpleEffectSchema> runeEffects = [];

        List<(FightEntity entity, List<SimpleEffectSchema> effects, bool isPlayer)> participants =
        [
            (monster, monster.effects, false),
            (characterSchema, runeEffects, true),
        ];

        participants.Sort((a, b) => b.entity.Initiative.CompareTo(a.entity.Initiative));

        for (int i = 0; i < iterations; i++)
        {
            var remainingPlayerHp = playerFullHp ? characterSchema.MaxHp : characterSchema.Hp;
            var remainingMonsterHp = monster.Hp;

            FightResult? outcome = null;

            int turns = 0;

            while (outcome is null)
            {
                foreach (var attacker in participants)
                {
                    List<FightSimUtility> potionEffectsForTurn = [];

                    foreach (var potion in potions)
                    {
                        if (potion.Quantity > 0)
                        {
                            potionEffectsForTurn.Add(
                                new FightSimUtility
                                {
                                    Item = potion.Item,
                                    Quantity = potion.Quantity,
                                    OriginalQuantity = potion.OriginalQuantity,
                                }
                            );
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
                        turns
                    );

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
                        break;
                    }
                    turns++;
                }
            }

            // while (outcome is null)
            // {
            //     turns++;
            //     if (remainingPlayerHp <= 0)
            //     {
            //         outcome = FightResult.Loss;
            //         break;
            //     }
            //     else if (remainingMonsterHp <= 0)
            //     {
            //         outcome = FightResult.Win;
            //         break;
            //     }

            //     var playerDamage = CalculateTurnDamage(characterSchema, monster);

            //     remainingMonsterHp -= playerDamage.damage;

            //     if (remainingMonsterHp <= 0)
            //     {
            //         outcome = FightResult.Win;
            //         break;
            //     }

            //     var monsterDamage = CalculateTurnDamage(monster, characterSchema);

            //     remainingPlayerHp -= monsterDamage.damage;

            //     if (remainingPlayerHp <= 0)
            //     {
            //         outcome = FightResult.Loss;
            //         break;
            //     }
            // }
            outcomes.Add(
                new FightOutcome
                {
                    Result = outcome ?? FightResult.Loss, // Should not be necessary
                    PlayerHp = remainingPlayerHp,
                    MonsterHp = remainingMonsterHp,
                    TotalTurns = turns,
                    ShouldFight =
                        outcome == FightResult.Win
                        && remainingPlayerHp >= (characterSchema.MaxHp * 0.15),
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
            (amountWon / fightSimulations) > 0.85 ? FightResult.Win : FightResult.Loss;

        return new FightOutcome
        {
            Result = generallyWon,
            ShouldFight = generallyWon == FightResult.Win,
            PlayerHp = playerHp,
            MonsterHp = monsterHp,
            TotalTurns = totalTurns,
        };
    }

    public static FightOutcome CalculateFightOutcomeWithBestEquipment(
        PlayerCharacter character,
        MonsterSchema monster,
        GameState gameState
    )
    {
        var result = FindBestFightEquipment(character, gameState, monster);

        return result.Item2;
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
        if (totalTurns == 1 || totalTurns == 2)
        {
            SimpleEffectSchema? burn = attackerRuneEffects.FirstOrDefault(effect =>
                effect.Code == Effect.Burn
            );

            if (burn is not null)
            {
                // Decrease burn damage by 10% each turn. So if burn value is 20%, then it's 20, 10, 0.
                double burnFactor = Math.Max((burn.Value * 0.01) - ((totalTurns - 1) * 0.1), 0);

                int burnDamage =
                    burnFactor > 0 ? (int)Math.Round(damageWithEffects * burnFactor) : 0;

                damageWithEffects += burnDamage;
            }
        }

        foreach (var effect in attackerPotionEffects)
        {
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

    public static (CharacterSchema, FightOutcome, List<EquipmentSlot>) FindBestFightEquipment(
        PlayerCharacter character,
        GameState gameState,
        MonsterSchema monster
    )
    {
        List<(string, string)> equipmentTypes =
        [
            ("weapon", "WeaponSlot"),
            ("body_armor", "BodyArmorSlot"),
            ("leg_armor", "LegArmorSlot"),
            ("helmet", "HelmetSlot"),
            ("boots", "BootsSlot"),
            ("ring", "Ring1Slot"),
            ("ring", "Ring2Slot"),
            ("amulet", "AmuletSlot"),
            ("shield", "ShieldSlot"),
        ];

        List<EquipmentSlot> itemsToEquip = [];

        /*
          This might not be the most optimal, but basically we go through each item type one by one, and find the best fit for every item to equip.
          There are definitely cases we don't handle super well by doing this, because the characer might have a fire weapon, that will be better
          with a specific armor set, because it gives more fire damage, but we will never consider that scenario, because the fire weapon might be
          disqualified in the "weapon" round, because it's not the best item.
          
          We will need a recursive function that calculates all combinations, but for now, this will be good enough to ensure that the characters
          put on their equipment before fighting, if they have any in their inventory, and in general will use decent equipment.
        */
        var bestSchemaCandiate = character.Schema with
        { };

        bestSchemaCandiate.Hp = bestSchemaCandiate.MaxHp;

        var bestFightOutcome = CalculateFightOutcome(bestSchemaCandiate, monster);

        int bestItemAmount = 1;

        foreach (var (equipmentType, equipmentSlot) in equipmentTypes)
        {
            var items = character.GetItemsFromInventoryWithType(equipmentType);

            if (items.Count == 0)
            {
                continue;
            }

            EquipmentSlot? equippedItem = null;

            switch (character.GetEquipmentSlot(equipmentSlot).Value)
            {
                case AppError error:
                    throw new Exception(error.Message);
                case EquipmentSlot slot:
                    equippedItem = slot;
                    break;
            }

            ItemSchema? bestItemCandidate = equippedItem is not null
                ? gameState.ItemsDict.GetValueOrNull(equippedItem.Code)
                : null;

            string? initialItemCode = bestItemCandidate?.Code;

            // if (bestItemCandidate is null)
            // {
            //     throw new Exception(
            //         $"Currently best weapon with code \"{character.Schema.WeaponSlot}\" is null"
            //     );
            // }

            foreach (var item in items)
            {
                ItemSchema? itemSchema = gameState.ItemsDict.GetValueOrNull(item.Item.Code);

                if (itemSchema is null)
                {
                    throw new Exception(
                        $"Current weapon with code \"{item.Item.Code}\" is null - should never happen"
                    );
                }

                if (!ItemService.CanUseItem(itemSchema, character.Schema))
                {
                    continue;
                }

                var characterSchema = bestSchemaCandiate with { };

                characterSchema = PlayerActionService.SimulateItemEquip(
                    characterSchema,
                    bestItemCandidate,
                    itemSchema,
                    equipmentSlot,
                    1
                );

                var fightOutcome = CalculateFightOutcome(characterSchema, monster);

                if (
                    bestItemCandidate is null
                    || fightOutcome.Result == FightResult.Win
                        && (
                            bestFightOutcome.Result != FightResult.Win
                            || fightOutcome.PlayerHp > bestFightOutcome.PlayerHp
                        )
                )
                {
                    bestFightOutcome = fightOutcome;
                    bestItemCandidate = item.Item;
                    bestSchemaCandiate = characterSchema;
                    bestItemAmount = item.Item.Subtype == "utility" ? item.Quantity : 1;
                }
            }

            if (bestItemCandidate is not null && initialItemCode != bestItemCandidate.Code)
            {
                string snakeCaseSlot = equipmentSlot.Replace("Slot", "").FromPascalToSnakeCase();

                logger.LogInformation(
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
        }

        return (bestSchemaCandiate, bestFightOutcome, itemsToEquip);
    }

    static void ApplyPreFightEffects(
        CharacterSchema characterSchema,
        GameState gameState,
        List<FightSimUtility> potions
    )
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
