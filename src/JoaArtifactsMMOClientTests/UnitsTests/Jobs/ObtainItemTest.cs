using Application;
using Application.Artifacts.Schemas;
using Application.ArtifactsApi.Schemas;
using Application.Jobs;
using JoaArtifactsMMOClientTests.Helpers;

namespace JoaArtifactsMMOClientTests;

public class ObtainItemTest
{
    [Fact(
        DisplayName = "Should only need 1 iteration to get 10 iron bars (100 items in total), if the character has 150 inventory space"
    )]
    public void CalculateObtainItemIterationsTest_10_IronBars_150_InventorySpace()
    {
        GameState gameState = ServiceHelper.GetPopulatedGameState();
        var character = PlayerCharacterHelper.GetFighterCharacter(gameState, 1);
        character.Schema.InventoryMaxItems = 150;

        List<int> iterations = ObtainItem.CalculateObtainItemIterations(
            GetIronBar(),
            character.GetAvailableInventorySpace(),
            10
        );

        int allItemsToObtain = iterations.Sum((iteration) => iteration);

        Assert.Equal(10, allItemsToObtain);
        Assert.Single(iterations);
    }

    [Fact(
        DisplayName = "Should need 2 iterations to get 10 iron bars (100 items in total), if the character has 100 inventory space"
    )]
    public void CalculateObtainItemIterationsTest_10_IronBars_100_InventorySpace()
    {
        GameState gameState = ServiceHelper.GetPopulatedGameState();
        var character = PlayerCharacterHelper.GetFighterCharacter(gameState, 1);
        character.Schema.InventoryMaxItems = 100;

        List<int> iterations = ObtainItem.CalculateObtainItemIterations(
            GetIronBar(),
            character.GetAvailableInventorySpace(),
            10
        );

        int allItemsToObtain = iterations.Sum((iteration) => iteration);

        Assert.Equal(10, allItemsToObtain);
        Assert.Equal(2, iterations.Count());
    }

    [Fact(
        DisplayName = "Should need 2 iterations to get 10 iron bars (100 items in total), if the character has 100 inventory space"
    )]
    public void CalculateObtainItemIterationsTest_13_IronBars_100_InventorySpace()
    {
        GameState gameState = ServiceHelper.GetPopulatedGameState();
        var character = PlayerCharacterHelper.GetFighterCharacter(gameState, 1);
        character.Schema.InventoryMaxItems = 100;

        List<int> iterations = ObtainItem.CalculateObtainItemIterations(
            GetIronBar(),
            character.GetAvailableInventorySpace(),
            13
        );

        int allItemsToObtain = iterations.Sum((iteration) => iteration);

        Assert.Equal(13, allItemsToObtain);
        Assert.Equal(2, iterations.Count);
    }

    public static ItemSchema GetIronBar()
    {
        return new ItemSchema
        {
            Name = "Iron Bar",
            Code = "iron_bar",
            Level = 10,
            Type = "resource",
            Subtype = "bar",
            Description =
                "A solid bar of refined iron, ready for crafting into weapons, armor, and tools.",
            Conditions = [],
            Effects = [],
            Craft = new CraftDto
            {
                Skill = Skill.Mining,
                Level = 10,
                Items = new List<DropSchema>
                {
                    new DropSchema { Code = "iron_ore", Quantity = 10 },
                },
                Quantity = 1,
            },
            Tradeable = true,
        };
    }
}
