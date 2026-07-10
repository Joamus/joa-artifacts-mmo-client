using Application.Artifacts.Schemas;
using Application.ArtifactsApi.Schemas;
using Application.Services;

public class CraftinServiceTest
{
    [Fact(
        DisplayName = "Crafting a level 15 item at 19 weapon crafting, should award more XP than crafting a level 10 item"
    )]
    public void FindBestFightEquipment_ShouldUseWeaponWithMoreDamage_AgainstYellowSlime_AtLevel1()
    {
        var testAirDagger = new ItemSchema
        {
            Name = "Test air dagger",
            Code = "test_air_dagger",
            Level = 10,
            Type = "weapon",
            Subtype = "",
            Description = "",
            Conditions = [],
            Effects =
            [
                new SimpleEffectSchema { Code = "attack_air", Value = 7 },
                new SimpleEffectSchema { Code = "critical_strike", Value = 0 },
            ],
            Craft = new CraftDto
            {
                Skill = Skill.Weaponcrafting,
                Level = 1,
                Items = [new DropSchema { Code = "copper_bar", Quantity = 6 }],
                Quantity = 1,
            },
            Tradeable = true,
        };

        var testEarthDagger = new ItemSchema
        {
            Name = "Test earth dagger",
            Code = "test_earth_dagger",
            Level = 15,
            Type = "weapon",
            Subtype = "",
            Description = "",
            Conditions = [],
            Effects =
            [
                new SimpleEffectSchema { Code = "attack_earth", Value = 8 },
                new SimpleEffectSchema { Code = "critical_strike", Value = 0 },
            ],
            Craft = new CraftDto
            {
                Skill = Skill.Weaponcrafting,
                Level = 1,
                Items =
                [
                    new DropSchema { Code = "wooden_stick", Quantity = 1 },
                    new DropSchema { Code = "ash_plank", Quantity = 4 },
                ],
                Quantity = 1,
            },
            Tradeable = true,
        };

        int skillLevel = 15;

        var lowLevelItemResult = CraftingService.GetXpForCraftingItem(skillLevel, testAirDagger);
        var highLevelItemResult = CraftingService.GetXpForCraftingItem(skillLevel, testEarthDagger);

        Assert.True(lowLevelItemResult < highLevelItemResult);
    }
}
