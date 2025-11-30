using Application;
using Application.Artifacts.Schemas;
using Application.ArtifactsApi.Schemas;
using Application.Character;
using Application.Records;
using Application.Services;
using Applicaton.Services.FightSimulator;
using JoaArtifactsMMOClientTests.Helpers;

namespace JoaArtifactsMMOClientTests;

public class FightSimulatorTest
{
    [Fact(
        DisplayName = "A level 1 character should use 'test_air_dagger' against 'yellow_slime', and not 'test_earth_dagger', because 'yellow_slime' has earth resistance"
    )]
    public void FindBestFightEquipment_ShouldUseWeaponWithMoreDamage_AgainstYellowSlime_AtLevel1()
    {
        GameState gameState = ServiceHelper.GetEmptyGameState();

        var yellowSlime = new MonsterSchema
        {
            Name = "Yellow Slime",
            Code = "yellow_slime",
            Level = 2,
            Type = MonsterType.Normal,
            Hp = 70,
            AttackFire = 0,
            AttackEarth = 8,
            AttackWater = 0,
            AttackAir = 0,
            ResFire = 0,
            ResEarth = 25,
            ResWater = 0,
            ResAir = 0,
            CriticalStrike = 0,
            Initiative = 50,
            Effects = [],
            MinGold = 0,
            MaxGold = 5,
        };

        var character = PlayerCharacterHelper.GetFighterCharacter();

        var testAirDagger = new ItemSchema
        {
            Name = "Test air dagger",
            Code = "test_air_dagger",
            Level = 1,
            Type = "weapon",
            Subtype = "",
            Description = "",
            Conditions = [],
            Effects = new List<SimpleEffectSchema>
            {
                new SimpleEffectSchema { Code = "attack_air", Value = 7 },
                new SimpleEffectSchema { Code = "critical_strike", Value = 0 },
            },
            Craft = new CraftDto
            {
                Skill = Skill.Weaponcrafting,
                Level = 1,
                Items = new List<DropSchema>
                {
                    new DropSchema { Code = "copper_bar", Quantity = 6 },
                },
                Quantity = 1,
            },
            Tradeable = true,
        };

        var testEarthDagger = new ItemSchema
        {
            Name = "Test earth dagger",
            Code = "test_earth_dagger",
            Level = 1,
            Type = "weapon",
            Subtype = "",
            Description = "",
            Conditions = [],
            Effects = new List<SimpleEffectSchema>
            {
                new SimpleEffectSchema { Code = "attack_earth", Value = 8 },
                new SimpleEffectSchema { Code = "critical_strike", Value = 0 },
            },
            Craft = new CraftDto
            {
                Skill = Skill.Weaponcrafting,
                Level = 1,
                Items = new List<DropSchema>
                {
                    new DropSchema { Code = "wooden_stick", Quantity = 1 },
                    new DropSchema { Code = "ash_plank", Quantity = 4 },
                },
                Quantity = 1,
            },
            Tradeable = true,
        };

        gameState.Items.Add(testAirDagger);
        gameState.ItemsDict[testAirDagger.Code] = testAirDagger;
        gameState.Items.Add(testEarthDagger);
        gameState.ItemsDict[testEarthDagger.Code] = testEarthDagger;

        List<ItemInInventory> itemsInInventory = new List<ItemInInventory>
        {
            new ItemInInventory { Item = testAirDagger, Quantity = 1 },
            new ItemInInventory { Item = testEarthDagger, Quantity = 1 },
        };

        var result = FightSimulator.FindBestFightEquipment(
            character,
            gameState,
            yellowSlime,
            itemsInInventory
        );

        Assert.True(result.ItemsToEquip.Exists(item => item.Code == testAirDagger.Code));
        Assert.True(result.Schema.WeaponSlot == testAirDagger.Code);
    }

    [Fact(
        DisplayName = "Should use 'test_air_dagger' against 'yellow_slime', with the already equipped 'dmg_jacket', because the alternative body armor is worse. Current weapon is 'test_earth_dagger'"
    )]
    public void FindBestFightEquipment_ShouldKeepEquipment_BecauseItIsBest()
    {
        GameState gameState = ServiceHelper.GetEmptyGameState();

        var yellowSlime = new MonsterSchema
        {
            Name = "Yellow Slime",
            Code = "yellow_slime",
            Level = 2,
            Type = MonsterType.Normal,
            Hp = 70,
            AttackFire = 0,
            AttackEarth = 8,
            AttackWater = 0,
            AttackAir = 0,
            ResFire = 0,
            ResEarth = 25,
            ResWater = 0,
            ResAir = 0,
            CriticalStrike = 0,
            Initiative = 50,
            Effects = [],
            MinGold = 0,
            MaxGold = 5,
        };

        var character = PlayerCharacterHelper.GetFighterCharacter();

        var testAirDagger = new ItemSchema
        {
            Name = "Test air dagger",
            Code = "test_air_dagger",
            Level = 1,
            Type = "weapon",
            Subtype = "",
            Description = "",
            Conditions = [],
            Effects = new List<SimpleEffectSchema>
            {
                new SimpleEffectSchema { Code = "attack_air", Value = 7 },
                new SimpleEffectSchema { Code = "critical_strike", Value = 0 },
            },
            Craft = new CraftDto
            {
                Skill = Skill.Weaponcrafting,
                Level = 1,
                Items = new List<DropSchema>
                {
                    new DropSchema { Code = "copper_bar", Quantity = 6 },
                },
                Quantity = 1,
            },
            Tradeable = true,
        };

        var testEarthDagger = new ItemSchema
        {
            Name = "Test earth dagger",
            Code = "test_earth_dagger",
            Level = 1,
            Type = "weapon",
            Subtype = "",
            Description = "",
            Conditions = [],
            Effects = new List<SimpleEffectSchema>
            {
                new SimpleEffectSchema { Code = "attack_earth", Value = 8 },
                new SimpleEffectSchema { Code = "critical_strike", Value = 0 },
            },
            Craft = new CraftDto
            {
                Skill = Skill.Weaponcrafting,
                Level = 1,
                Items = new List<DropSchema>
                {
                    new DropSchema { Code = "wooden_stick", Quantity = 1 },
                    new DropSchema { Code = "ash_plank", Quantity = 4 },
                },
                Quantity = 1,
            },
            Tradeable = true,
        };

        var dmgJacket = new ItemSchema
        {
            Name = "Dmg jacket",
            Code = "dmg_jacket",
            Level = 1,
            Type = "body_armor",
            Subtype = "",
            Description = "",
            Conditions = [],
            Effects = new List<SimpleEffectSchema>
            {
                new SimpleEffectSchema { Code = Effect.Damage, Value = 10 },
            },
            Craft = new CraftDto
            {
                Skill = Skill.Gearcrafting,
                Level = 1,
                Items = new List<DropSchema>
                {
                    new DropSchema { Code = "wooden_stick", Quantity = 1 },
                    new DropSchema { Code = "ash_plank", Quantity = 4 },
                },
                Quantity = 1,
            },
            Tradeable = true,
        };

        var worseDmgJacket = new ItemSchema
        {
            Name = "Worse dmg jacket",
            Code = "worse_dmg_jacket",
            Level = 1,
            Type = "body_armor",
            Subtype = "",
            Description = "",
            Conditions = [],
            Effects = new List<SimpleEffectSchema>
            {
                new SimpleEffectSchema { Code = Effect.Damage, Value = 5 },
            },
            Craft = new CraftDto
            {
                Skill = Skill.Gearcrafting,
                Level = 1,
                Items = new List<DropSchema>
                {
                    new DropSchema { Code = "wooden_stick", Quantity = 1 },
                    new DropSchema { Code = "ash_plank", Quantity = 4 },
                },
                Quantity = 1,
            },
            Tradeable = true,
        };

        gameState.Items.Add(testAirDagger);
        gameState.ItemsDict[testAirDagger.Code] = testAirDagger;

        gameState.Items.Add(testEarthDagger);
        gameState.ItemsDict[testEarthDagger.Code] = testEarthDagger;

        gameState.Items.Add(dmgJacket);
        gameState.ItemsDict[dmgJacket.Code] = dmgJacket;

        gameState.Items.Add(worseDmgJacket);
        gameState.ItemsDict[worseDmgJacket.Code] = worseDmgJacket;

        List<ItemInInventory> itemsInInventory = new List<ItemInInventory>
        {
            new ItemInInventory { Item = testAirDagger, Quantity = 1 },
            new ItemInInventory { Item = worseDmgJacket, Quantity = 1 },
        };

        character.Schema.WeaponSlot = testEarthDagger.Code;
        character.Schema.BodyArmorSlot = dmgJacket.Code;

        PlayerActionService.SimulateItemEquip(
            character.Schema,
            null,
            dmgJacket,
            "BodyArmorSlot",
            1
        );
        PlayerActionService.SimulateItemEquip(
            character.Schema,
            null,
            testEarthDagger,
            "WeaponSlot",
            1
        );

        var result = FightSimulator.FindBestFightEquipment(
            character,
            gameState,
            yellowSlime,
            itemsInInventory
        );

        Assert.True(result.ItemsToEquip.Count() == 1);
        Assert.True(result.ItemsToEquip.Exists(item => item.Code == testAirDagger.Code));
        Assert.True(result.Schema.BodyArmorSlot == dmgJacket.Code);
    }
}

// }
