using Application;
using Application.ArtifactsApi.Schemas;
using Application.Character;
using Application.Records;
using Applicaton.Services.FightSimulator;
using JoaArtifactsMMOClientTests.Helpers;
using NSubstitute;
using OneOf.Types;

namespace JoaArtifactsMMOClientTests;

public class FightSimulatorTest
{
    [Fact(
        DisplayName = "Should use 'copper_dagger' against 'yellow_slime', instead of 'wooden_staff', due to its earth resistance"
    )]
    public void FindBestFightEquipment_ShouldUseBestWeapon()
    {
        GameState gameState = ServiceHelper.GetPopulatedGameState();

        var yellowSlime = gameState.MonstersDict["yellow_slime"];

        var character = PlayerCharacterHelper.GetFighterCharacter(gameState, 1);

        var copperDagger = gameState.ItemsDict["copper_dagger"];
        var woodenStaff = gameState.ItemsDict["wooden_staff"];

        List<ItemInInventory> itemsInInventory =
        [
            new() { Item = copperDagger, Quantity = 1 },
            new() { Item = woodenStaff, Quantity = 1 },
        ];

        var result = FightSimulator
            .FindBestFightEquipment(character, gameState, yellowSlime, itemsInInventory)
            .SimResult;

        Assert.Single(result.ItemsToEquip);
        Assert.True(result.ItemsToEquip.Exists(item => item.Code == copperDagger.Code));
    }

    [Fact(
        DisplayName = "Should use 'iron_armor' over 'copper_armor', because it gives more health"
    )]
    public void FindBestFightEquipment_ShouldUseHighestHpArmor()
    {
        GameState gameState = ServiceHelper.GetPopulatedGameState();

        var yellowSlime = gameState.MonstersDict["yellow_slime"];

        var character = PlayerCharacterHelper.GetFighterCharacter(gameState, 10);

        var woodenStaff = gameState.ItemsDict["wooden_staff"];
        var copperArmor = gameState.ItemsDict["copper_armor"];
        var ironArmor = gameState.ItemsDict["iron_armor"];

        List<ItemInInventory> itemsInInventory =
        [
            new() { Item = woodenStaff, Quantity = 1 },
            new() { Item = copperArmor, Quantity = 1 },
            new() { Item = ironArmor, Quantity = 1 },
        ];

        var result = FightSimulator
            .FindBestFightEquipment(character, gameState, yellowSlime, itemsInInventory)
            .SimResult;

        Assert.Equal(2, result.ItemsToEquip.Count);
        Assert.True(result.ItemsToEquip.Exists(item => item.Code == woodenStaff.Code));
        Assert.True(result.ItemsToEquip.Exists(item => item.Code == ironArmor.Code));
    }

    [Fact(DisplayName = "Should not use 'small_health_potion', because the fight is too easy")]
    public void FindBestFightEquipment_ShouldNotUsePotion()
    {
        GameState gameState = ServiceHelper.GetPopulatedGameState();

        var yellowSlime = gameState.MonstersDict["yellow_slime"];

        var character = PlayerCharacterHelper.GetFighterCharacter(gameState, 10);

        var copperDagger = gameState.ItemsDict["copper_dagger"];
        var ironArmor = gameState.ItemsDict["iron_armor"];
        var smallHealthPotion = gameState.ItemsDict["small_health_potion"];

        List<ItemInInventory> itemsInInventory =
        [
            new() { Item = copperDagger, Quantity = 1 },
            new() { Item = ironArmor, Quantity = 1 },
            new() { Item = smallHealthPotion, Quantity = 100 },
        ];

        var result = FightSimulator
            .FindBestFightEquipment(character, gameState, yellowSlime, itemsInInventory)
            .SimResult;

        Assert.Equal(2, result.ItemsToEquip.Count);
        Assert.True(result.ItemsToEquip.Exists(item => item.Code == copperDagger.Code));
        Assert.True(result.ItemsToEquip.Exists(item => item.Code == ironArmor.Code));
    }

    [Fact(DisplayName = "Should use 'small_health_potion', because the fight requires it")]
    public void FindBestFightEquipment_ShouldUsePotion()
    {
        GameState gameState = ServiceHelper.GetPopulatedGameState();

        var monster = gameState.MonstersDict["flying_snake"];

        var character = PlayerCharacterHelper.GetFighterCharacter(gameState, 10);

        var copperDagger = gameState.ItemsDict["copper_dagger"];
        var ironArmor = gameState.ItemsDict["iron_armor"];
        var smallHealthPotion = gameState.ItemsDict["small_health_potion"];

        List<ItemInInventory> itemsInInventory =
        [
            new() { Item = copperDagger, Quantity = 1 },
            new() { Item = ironArmor, Quantity = 1 },
            new() { Item = smallHealthPotion, Quantity = 100 },
        ];

        var result = FightSimulator
            .FindBestFightEquipment(character, gameState, monster, itemsInInventory)
            .SimResult;

        Assert.Equal(3, result.ItemsToEquip.Count);
        Assert.True(result.ItemsToEquip.Exists(item => item.Code == copperDagger.Code));
        Assert.True(result.ItemsToEquip.Exists(item => item.Code == ironArmor.Code));
        Assert.True(result.ItemsToEquip.Exists(item => item.Code == smallHealthPotion.Code));
    }

    [Fact(
        DisplayName = "Should use 'small_health_potion' and 'air_boost_potion', because the outcome ends up using less potions"
    )]
    public void FindBestFightEquipment_ShouldUseHpPotionAndBoost()
    {
        GameState gameState = ServiceHelper.GetPopulatedGameState();

        var monster = gameState.MonstersDict["flying_snake"];

        var character = PlayerCharacterHelper.GetFighterCharacter(gameState, 10);

        var weapon = gameState.ItemsDict["sticky_dagger"];
        var armor = gameState.ItemsDict["feather_coat"];
        var smallHealthPotion = gameState.ItemsDict["small_health_potion"];
        var airBoostPotion = gameState.ItemsDict["air_boost_potion"];

        int SimWithBoost()
        {
            List<ItemInInventory> itemsInInventory =
            [
                new() { Item = weapon, Quantity = 1 },
                new() { Item = armor, Quantity = 1 },
                new() { Item = gameState.ItemsDict["leather_boots"], Quantity = 1 },
                new() { Item = gameState.ItemsDict["leather_hat"], Quantity = 1 },
                new() { Item = gameState.ItemsDict["leather_legs_armor"], Quantity = 1 },
                new() { Item = gameState.ItemsDict["forest_ring"], Quantity = 2 },
                new() { Item = smallHealthPotion, Quantity = 100 },
                new() { Item = airBoostPotion, Quantity = 100 },
            ];

            var result = FightSimulator
                .FindBestFightEquipment(character, gameState, monster, itemsInInventory)
                .SimResult;

            Assert.Equal(itemsInInventory.Count, result.ItemsToEquip.Count - 1); // we are equipping rings, which is in two seperate entries here
            Assert.True(result.ItemsToEquip.Exists(item => item.Code == weapon.Code));
            Assert.True(result.ItemsToEquip.Exists(item => item.Code == armor.Code));
            Assert.True(result.ItemsToEquip.Exists(item => item.Code == smallHealthPotion.Code));
            Assert.True(result.ItemsToEquip.Exists(item => item.Code == airBoostPotion.Code));

            return result.Outcome.PotionsUsed;
        }

        int SimWithoutBoost()
        {
            List<ItemInInventory> itemsInInventory =
            [
                new() { Item = weapon, Quantity = 1 },
                new() { Item = armor, Quantity = 1 },
                new() { Item = gameState.ItemsDict["leather_boots"], Quantity = 1 },
                new() { Item = gameState.ItemsDict["leather_hat"], Quantity = 1 },
                new() { Item = gameState.ItemsDict["leather_legs_armor"], Quantity = 1 },
                new() { Item = gameState.ItemsDict["forest_ring"], Quantity = 2 },
                new() { Item = smallHealthPotion, Quantity = 100 },
            ];

            var result = FightSimulator
                .FindBestFightEquipment(character, gameState, monster, itemsInInventory)
                .SimResult;

            Assert.False(result.Outcome.ShouldFight);
            Assert.True(result.ItemsToEquip.Exists(item => item.Code == weapon.Code));
            Assert.True(result.ItemsToEquip.Exists(item => item.Code == armor.Code));
            Assert.True(result.ItemsToEquip.Exists(item => item.Code == smallHealthPotion.Code));

            return result.Outcome.PotionsUsed;
        }

        int potionsUsedWithBoost = SimWithBoost();
        int potionsUsedWithoutBoost = SimWithoutBoost();
    }

    [Fact(
        DisplayName = "Should use better equipment from item list, than what is currently equipped"
    )]
    public void GetBetterItemsToWithdraw_ShouldWithdrawItemsFromBank()
    {
        GameState gameState = ServiceHelper.GetPopulatedGameState();

        var monster = gameState.MonstersDict["flying_snake"];

        var character = PlayerCharacterHelper.GetFighterCharacter(gameState, 30);

        var weapon = gameState.ItemsDict["hunting_bow"];
        var armor = gameState.ItemsDict["feather_coat"];

        List<DropSchema> bankItems =
        [
            new DropSchema { Code = "skull_wand", Quantity = 1 },
            new DropSchema { Code = "bandit_armor", Quantity = 1 },
            new DropSchema { Code = "snakeskin_legs_armor", Quantity = 1 },
        ];

        character.Schema = PlayerActionService.SimulateItemEquip(
            character.Schema,
            null,
            weapon,
            "WeaponSlot",
            1
        );
        character.Schema = PlayerActionService.SimulateItemEquip(
            character.Schema,
            null,
            armor,
            "BodyArmorSlot",
            1
        );

        var result = FightSimulator
            .FindBestFightEquipment(
                character,
                gameState,
                monster,
                [
                    .. bankItems.Select(item => new ItemInInventory
                    {
                        Item = gameState.ItemsDict[item.Code],
                        Quantity = item.Quantity,
                    }),
                ]
            )
            .SimResult;

        Assert.True(result.ItemsToEquip.Exists(item => item.Code == "skull_wand"));
        Assert.True(result.ItemsToEquip.Exists(item => item.Code == "bandit_armor"));
        Assert.True(result.ItemsToEquip.Exists(item => item.Code == "snakeskin_legs_armor"));
    }

    [Fact(
        DisplayName = "Should use better equipment from item list, than what is currently equipped"
    )]
    public void FindBestFightEquipment_UseBetterEquipmentIfWorseIsEquipped()
    {
        GameState gameState = ServiceHelper.GetPopulatedGameState();

        var monster = gameState.MonstersDict["flying_snake"];

        var character = PlayerCharacterHelper.GetFighterCharacter(gameState, 30);

        var weapon = gameState.ItemsDict["hunting_bow"];
        var armor = gameState.ItemsDict["feather_coat"];

        List<DropSchema> bankItems =
        [
            new DropSchema { Code = "skull_wand", Quantity = 1 },
            new DropSchema { Code = "bandit_armor", Quantity = 1 },
            new DropSchema { Code = "snakeskin_legs_armor", Quantity = 1 },
        ];

        character.Schema = PlayerActionService.SimulateItemEquip(
            character.Schema,
            null,
            weapon,
            "WeaponSlot",
            1
        );
        character.Schema = PlayerActionService.SimulateItemEquip(
            character.Schema,
            null,
            armor,
            "BodyArmorSlot",
            1
        );

        // gameState
        //     .BankItemCache.GetBankItems(Arg.Any<PlayerCharacter>(), Arg.Any<bool>())
        //     .Returns(call => bankItems);

        var result = FightSimulator
            .FindBestFightEquipment(
                character,
                gameState,
                monster,
                [
                    .. bankItems.Select(item => new ItemInInventory
                    {
                        Item = gameState.ItemsDict[item.Code],
                        Quantity = item.Quantity,
                    }),
                ]
            )
            .SimResult;

        Assert.True(result.ItemsToEquip.Exists(item => item.Code == "skull_wand"));
        Assert.True(result.ItemsToEquip.Exists(item => item.Code == "bandit_armor"));
        Assert.True(result.ItemsToEquip.Exists(item => item.Code == "snakeskin_legs_armor"));
    }

    [Fact(DisplayName = "Should find best equipment in GetItemsWorthSimming")]
    public void GetItemsWorthSimming_ShouldFindBestUpgrade()
    {
        GameState gameState = ServiceHelper.GetPopulatedGameState();

        List<ItemInInventory> items =
        [
            // Items that are downgrades
            new ItemInInventory { Item = gameState.ItemsDict["life_amulet"], Quantity = 1 }, // downgrade from dreadful_amulet
            // These items should not overlap
            new ItemInInventory { Item = gameState.ItemsDict["dreadful_amulet"], Quantity = 1 },
            new ItemInInventory { Item = gameState.ItemsDict["skull_wand"], Quantity = 1 },
            new ItemInInventory { Item = gameState.ItemsDict["bandit_armor"], Quantity = 1 },
            new ItemInInventory { Item = gameState.ItemsDict["iron_armor"], Quantity = 1 },
            new ItemInInventory { Item = gameState.ItemsDict["copper_legs_armor"], Quantity = 1 },
            new ItemInInventory { Item = gameState.ItemsDict["iron_ring"], Quantity = 1 },
            new ItemInInventory
            {
                Item = gameState.ItemsDict["snakeskin_legs_armor"],
                Quantity = 1,
            },
        ];

        var result = FightSimulator.GetItemsWorthSimming(items);

        // These items are downgrades, the exact same or worse stats
        Assert.False(result.Exists(item => item.Item.Code == "life_amulet"));
        // Similar, but different crit value so keep
        Assert.True(result.Exists(item => item.Item.Code == "dreadful_amulet"));
        Assert.True(result.Exists(item => item.Item.Code == "skull_wand"));
        Assert.True(result.Exists(item => item.Item.Code == "bandit_armor"));
        Assert.True(result.Exists(item => item.Item.Code == "iron_armor"));
        Assert.True(result.Exists(item => item.Item.Code == "copper_legs_armor"));
        Assert.True(result.Exists(item => item.Item.Code == "iron_ring"));
        Assert.True(result.Exists(item => item.Item.Code == "snakeskin_legs_armor"));
    }

    [Fact(DisplayName = "Should win the boss fight against KingSlime")]
    public void SimulateBossFightOutcome_ShouldWinAgainstKingSlime()
    {
        GameState gameState = ServiceHelper.GetPopulatedGameState();

        var mainCharacter = PlayerCharacterHelper.GetFighterCharacter(gameState, 15);

        var helperCharacter1 = PlayerCharacterHelper.GetFighterCharacter(gameState, 15);

        var helperCharacter2 = PlayerCharacterHelper.GetFighterCharacter(gameState, 15);

        List<PlayerCharacter> allCharacters = [mainCharacter, helperCharacter1, helperCharacter2];

        List<DropSchema> bankItems =
        [
            // Earth item load out - for two characters, since second wep is fire/earth
            new DropSchema { Code = "iron_sword", Quantity = 3 },
            new DropSchema { Code = "mushmush_bow", Quantity = 3 },
            new DropSchema { Code = "iron_armor", Quantity = 3 },
            new DropSchema { Code = "iron_boots", Quantity = 3 },
            new DropSchema { Code = "iron_legs_armor", Quantity = 3 },
            new DropSchema { Code = "iron_helm", Quantity = 3 },
            new DropSchema { Code = "iron_shield", Quantity = 3 },
            // Air item load out
            new DropSchema { Code = "highwayman_dagger", Quantity = 3 },
            new DropSchema { Code = "leather_armor", Quantity = 3 },
            new DropSchema { Code = "leather_boots", Quantity = 3 },
            new DropSchema { Code = "leather_legs_armor", Quantity = 3 },
            new DropSchema { Code = "leather_hat", Quantity = 3 },
            new DropSchema { Code = "air_ring", Quantity = 6 },
            // For all
            new DropSchema { Code = "iron_ring", Quantity = 6 },
            new DropSchema { Code = "forest_ring", Quantity = 6 },
            new DropSchema { Code = "novice_guide", Quantity = 3 },
            new DropSchema { Code = "life_amulet", Quantity = 3 },
            new DropSchema { Code = "small_health_potion", Quantity = 300 },
            new DropSchema { Code = "minor_health_potion", Quantity = 300 },
            new DropSchema { Code = "earth_boost_potion", Quantity = 300 },
            new DropSchema { Code = "air_boost_potion", Quantity = 300 },
            new DropSchema { Code = "water_boost_potion", Quantity = 300 },
        ];

        var monster = gameState.MonstersDict["king_slime"];

        var bossResults = FightSimulator.SimulateBossFightOutcome(
            mainCharacter,
            [helperCharacter1, helperCharacter2],
            gameState,
            bankItems,
            monster
        );

        Assert.True(
            bossResults.Exists(result =>
                result.Outcome.ShouldFight && result.ItemsToEquip.Count > 0
            )
        );

        foreach (var item in bankItems)
        {
            int totalAmountOfItemOnCharacters = bossResults.Sum(result =>
            {
                int sum = 0;

                foreach (var itemToEquip in result.ItemsToEquip)
                {
                    if (itemToEquip.Code == item.Code)
                    {
                        sum += itemToEquip.Quantity;
                    }
                }

                return sum;
            });

            Assert.True(totalAmountOfItemOnCharacters <= item.Quantity);
        }
    }

    [Fact(DisplayName = "Should win the boss fight against Pixie")]
    public void SimulateBossFightOutcome_ShouldWinAgainstPixie()
    {
        GameState gameState = ServiceHelper.GetPopulatedGameState();

        var mainCharacter = PlayerCharacterHelper.GetFighterCharacter(gameState, 45);

        var helperCharacter1 = PlayerCharacterHelper.GetFighterCharacter(gameState, 45);

        var helperCharacter2 = PlayerCharacterHelper.GetFighterCharacter(gameState, 45);

        List<PlayerCharacter> allCharacters = [mainCharacter, helperCharacter1, helperCharacter2];

        List<DropSchema> bankItems =
        [
            // Armor
            new DropSchema { Code = "cultist_hat", Quantity = 3 },
            new DropSchema { Code = "obsidian_helmet", Quantity = 3 },
            new DropSchema { Code = "hork_helmet", Quantity = 3 },
            new DropSchema { Code = "jester_hat", Quantity = 3 },
            new DropSchema { Code = "malefic_armor", Quantity = 3 },
            new DropSchema { Code = "dreadful_armor", Quantity = 3 },
            new DropSchema { Code = "obsidian_armor", Quantity = 3 },
            new DropSchema { Code = "enchanter_pants", Quantity = 3 },
            new DropSchema { Code = "mithril_platelegs", Quantity = 3 },
            new DropSchema { Code = "ancient_jean", Quantity = 3 },
            new DropSchema { Code = "enchanter_boots", Quantity = 3 },
            new DropSchema { Code = "lizard_boots", Quantity = 3 },
            new DropSchema { Code = "gold_boots", Quantity = 3 },
            // Amulet
            new DropSchema { Code = "masterful_necklace", Quantity = 3 },
            new DropSchema { Code = "diamond_amulet", Quantity = 3 },
            new DropSchema { Code = "greater_ruby_amulet", Quantity = 3 },
            new DropSchema { Code = "greater_dreadful_amulet", Quantity = 3 },
            // Shield
            new DropSchema { Code = "mithril_shield", Quantity = 3 },
            new DropSchema { Code = "dreadful_shield", Quantity = 3 },
            new DropSchema { Code = "gold_shield", Quantity = 3 },
            // Rune
            new DropSchema { Code = "burn_rune", Quantity = 3 },
            new DropSchema { Code = "healing_rune", Quantity = 3 },
            new DropSchema { Code = "healing_aura_rune", Quantity = 3 },
            new DropSchema { Code = "lifesteal_rune", Quantity = 3 },
            // Ring
            new DropSchema { Code = "malefic_ring", Quantity = 6 },
            new DropSchema { Code = "royal_skeleton_ring", Quantity = 6 },
            // Weapon
            new DropSchema { Code = "mithril_sword", Quantity = 3 },
            new DropSchema { Code = "bloodblade", Quantity = 3 },
            new DropSchema { Code = "wrathsword", Quantity = 3 },
            new DropSchema { Code = "dreadful_battleaxe", Quantity = 3 },
            // Potions
            new DropSchema { Code = "earth_boost_potion", Quantity = 300 },
            new DropSchema { Code = "fire_boost_potion", Quantity = 300 },
            new DropSchema { Code = "air_boost_potion", Quantity = 300 },
            new DropSchema { Code = "water_boost_potion", Quantity = 300 },
            new DropSchema { Code = "water_res_potion", Quantity = 300 },
            new DropSchema { Code = "air_res_potion", Quantity = 300 },
            new DropSchema { Code = "fire_res_potion", Quantity = 300 },
            new DropSchema { Code = "earth_res_potion", Quantity = 300 },
            new DropSchema { Code = "greater_health_potion", Quantity = 300 },
            new DropSchema { Code = "enhanced_health_potion", Quantity = 300 },
            new DropSchema { Code = "health_splash_potion", Quantity = 300 },
            new DropSchema { Code = "health_boost_potion", Quantity = 300 },
        ];

        var monster = gameState.MonstersDict["pixie"];

        var bossResults = FightSimulator.SimulateBossFightOutcome(
            mainCharacter,
            [helperCharacter1, helperCharacter2],
            gameState,
            bankItems,
            monster
        );

        Assert.True(
            bossResults.Exists(result =>
                result.Outcome.ShouldFight && result.ItemsToEquip.Count > 0
            )
        );

        foreach (var item in bankItems)
        {
            int totalAmountOfItemOnCharacters = bossResults.Sum(result =>
            {
                int sum = 0;

                foreach (var itemToEquip in result.ItemsToEquip)
                {
                    if (itemToEquip.Code == item.Code)
                    {
                        sum += itemToEquip.Quantity;
                    }
                }

                return sum;
            });

            Assert.True(totalAmountOfItemOnCharacters <= item.Quantity);
        }
    }
}
