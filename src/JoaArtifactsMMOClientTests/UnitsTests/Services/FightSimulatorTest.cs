using Application;
using Application.Records;
using Applicaton.Services.FightSimulator;
using JoaArtifactsMMOClientTests.Helpers;

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

        var copperDagger = gameState.ItemsDict["copper_dagger"];
        var woodenStaff = gameState.ItemsDict["wooden_staff"];
        var copperArmor = gameState.ItemsDict["copper_armor"];
        var ironArmor = gameState.ItemsDict["iron_armor"];

        List<ItemInInventory> itemsInInventory =
        [
            new() { Item = copperDagger, Quantity = 1 },
            new() { Item = woodenStaff, Quantity = 1 },
            new() { Item = copperArmor, Quantity = 1 },
            new() { Item = ironArmor, Quantity = 1 },
        ];

        var result = FightSimulator
            .FindBestFightEquipment(character, gameState, yellowSlime, itemsInInventory)
            .SimResult;

        Assert.Equal(2, result.ItemsToEquip.Count);
        Assert.True(result.ItemsToEquip.Exists(item => item.Code == copperDagger.Code));
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
}
