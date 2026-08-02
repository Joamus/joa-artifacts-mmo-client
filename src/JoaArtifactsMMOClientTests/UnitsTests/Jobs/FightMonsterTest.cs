using Application;
using Application.ArtifactsApi.Schemas;
using Application.Character;
using Application.Jobs;
using Application.Records;
using Application.Services.ApiServices;
using Applicaton.Services.FightSimulator;
using Infrastructure;
using JoaArtifactsMMOClientTests.Helpers;
using NSubstitute;
using NSubstitute.Extensions;

public class FightMonsterTest
{
    [Fact(
        DisplayName = "Should want to withdraw better equipment from item list, than what is currently equipped"
    )]
    public async Task GetBetterItemsToWithdraw_ShouldWithdrawItemsFromBank()
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

        gameState.Services.BankItemCache.GetBankItems(character).Returns(bankItems);

        var result = await FightMonster.GetBetterItemsToWithdraw(character, gameState, monster);

        Assert.True(result.Exists(item => item.Code == "skull_wand"));
        Assert.True(result.Exists(item => item.Code == "bandit_armor"));
        Assert.True(result.Exists(item => item.Code == "snakeskin_legs_armor"));
    }
}
