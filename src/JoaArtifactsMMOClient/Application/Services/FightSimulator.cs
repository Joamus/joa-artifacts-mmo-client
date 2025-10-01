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
    private static readonly double MONSTER_CRIT_BIAS = 1.25;

    private static ILogger<FightSimulator> logger = LoggerFactory
        .Create(AppLogger.options)
        .CreateLogger<FightSimulator>();

    public static FightOutcome CalculateFightOutcome(
        CharacterSchema character,
        MonsterSchema monster,
        bool playerFullHp = true
    )
    {
        var remainingPlayerHp = playerFullHp ? character.MaxHp : character.Hp;
        var remainingMonsterHp = monster.Hp;

        FightResult? outcome = null;

        int turns = 0;

        while (outcome is null)
        {
            turns++;
            if (remainingPlayerHp <= 0)
            {
                outcome = FightResult.Loss;
                break;
            }
            else if (remainingMonsterHp <= 0)
            {
                outcome = FightResult.Win;
                break;
            }

            int playerDamage = CalculatePlayerDamage(character, monster);

            remainingMonsterHp -= playerDamage;

            if (remainingMonsterHp <= 0)
            {
                outcome = FightResult.Win;
                break;
            }

            int monsterDamage = CalculateMonsterDamage(monster, character);

            remainingPlayerHp -= monsterDamage;

            if (remainingPlayerHp <= 0)
            {
                outcome = FightResult.Loss;
                break;
            }
        }

        // TODO: Implement
        return new FightOutcome
        {
            Result = outcome ?? FightResult.Loss, // Should not be necessary
            PlayerHp = remainingPlayerHp,
            MonsterHp = remainingMonsterHp,
            TotalTurns = turns,
            ShouldFight =
                outcome == FightResult.Win && remainingPlayerHp >= (character.MaxHp * 0.15),
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

    private static int CalculateMonsterDamage(MonsterSchema monster, CharacterSchema character)
    {
        int fireDamage = CalculateElementalAttack(
            monster.AttackFire,
            0,
            0,
            (int)(monster.CriticalStrike * MONSTER_CRIT_BIAS),
            character.ResFire
        );
        int earthDamage = CalculateElementalAttack(
            monster.AttackEarth,
            0,
            0,
            (int)(monster.CriticalStrike * MONSTER_CRIT_BIAS),
            character.ResEarth
        );
        int waterDamage = CalculateElementalAttack(
            monster.AttackWater,
            0,
            0,
            (int)(monster.CriticalStrike * MONSTER_CRIT_BIAS),
            character.ResWater
        );
        int airDamage = CalculateElementalAttack(
            monster.AttackAir,
            0,
            0,
            (int)(monster.CriticalStrike * MONSTER_CRIT_BIAS),
            character.ResAir
        );

        return fireDamage + earthDamage + waterDamage + airDamage;
    }

    private static int CalculatePlayerDamage(CharacterSchema character, MonsterSchema monster)
    {
        int fireDamage = CalculateElementalAttack(
            character.AttackFire,
            character.DmgFire,
            character.Dmg,
            character.CriticalStrike,
            monster.ResFire
        );
        int earthDamage = CalculateElementalAttack(
            character.AttackEarth,
            character.DmgEarth,
            character.Dmg,
            character.CriticalStrike,
            monster.ResEarth
        );
        int waterDamage = CalculateElementalAttack(
            character.AttackWater,
            character.DmgWater,
            character.Dmg,
            character.CriticalStrike,
            monster.ResWater
        );
        int airDamage = CalculateElementalAttack(
            character.AttackAir,
            character.DmgAir,
            character.Dmg,
            character.CriticalStrike,
            monster.ResAir
        );

        return fireDamage + earthDamage + waterDamage + airDamage;
    }

    private static int CalculateElementalAttack(
        int baseDamage,
        int elementalMultiplier,
        int damageMultiplier,
        int critChance,
        int resistance
    )
    {
        int damage = (int)
            Math.Round(baseDamage + baseDamage * (elementalMultiplier + damageMultiplier) * 0.01); // Not sure where the 0.01 is from

        damage = (int)(damage * 1 + (critChance * 0.01 * (1 + CRIT_DAMAGE_MODIFIER)));

        damage = (int)Math.Round(damage / (1 + resistance * 0.01));
        return damage;
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

                if (!ItemService.CanUseItem(itemSchema, character.Schema.Level))
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
                    fightOutcome.Result == FightResult.Win
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
}

public record FightOutcome
{
    public FightResult Result { get; init; }

    public int PlayerHp { get; init; }

    public int MonsterHp { get; init; }

    public int TotalTurns { get; init; }

    public bool ShouldFight { get; init; }
}
