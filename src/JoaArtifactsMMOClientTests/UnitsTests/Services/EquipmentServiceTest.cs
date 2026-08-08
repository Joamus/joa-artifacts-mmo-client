using Application;
using Application.ArtifactsApi.Schemas;
using Application.ArtifactsApi.Schemas.Responses;
using Application.Character;
using Application.Services;
using Application.Services.ApiServices;
using Infrastructure;
using JoaArtifactsMMOClientTests.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace JoaArtifactsMMOClientTests;

public class EquipmentServiceTest
{
    [Fact(
        DisplayName = "Should pick 'copper_dagger' as next improvement at level 1, when they have no weapon"
    )]
    public async Task EnsureFightEquipment_ShouldUseCopperDagger()
    {
        var bankItemCache = Substitute.For<BankItemCache>(
            Substitute.For<AccountRequester>(
                Substitute.For<ApiRequester>("1234", false),
                "test_account_name",
                Substitute.For<ILogger>()
            )
        );

        GameState gameState = ServiceHelper.GetPopulatedGameState(bankItemCache);

        List<DropSchema> bankItems = [];

        bankItemCache
            .GetBankItems(Arg.Any<PlayerCharacter>(), Arg.Any<bool>())
            .Returns(call => bankItems);

        var character = PlayerCharacterHelper.GetFighterCharacter(gameState, 1);

        var result = await EquipmentService.EnsureFightEquipment(character, gameState, bankItems);

        Assert.Equal("copper_dagger", result!.Code);
    }

    [Fact(
        DisplayName = "Should pick 'copper_helmet' as next improvement at level 1, when the character has a 'copper_dagger' available"
    )]
    public async Task EnsureFightEquipment_ShouldChooseCopperHelmet()
    {
        var bankItemCache = Substitute.For<BankItemCache>(
            Substitute.For<AccountRequester>(
                Substitute.For<ApiRequester>("1234", false),
                "test_account_name",
                Substitute.For<ILogger>()
            )
        );

        GameState gameState = ServiceHelper.GetPopulatedGameState(bankItemCache);

        List<DropSchema> bankItems = [new DropSchema { Code = "copper_dagger", Quantity = 1 }];

        bankItemCache
            .GetBankItems(Arg.Any<PlayerCharacter>(), Arg.Any<bool>())
            .Returns(call => bankItems);

        var character = PlayerCharacterHelper.GetFighterCharacter(gameState, 1);

        var result = await EquipmentService.EnsureFightEquipment(character, gameState, bankItems);

        Assert.Equal("copper_helmet", result!.Code);
    }

    [Fact(
        DisplayName = "Should not pick any new items at level 5, because the upgrade is too small"
    )]
    public async Task EnsureFightEquipment_ShouldNotPickAnyItemAtLevel5()
    {
        var bankItemCache = Substitute.For<BankItemCache>(
            Substitute.For<AccountRequester>(
                Substitute.For<ApiRequester>("1234", false),
                "test_account_name",
                Substitute.For<ILogger>()
            )
        );

        GameState gameState = ServiceHelper.GetPopulatedGameState(bankItemCache);

        List<DropSchema> bankItems =
        [
            new DropSchema { Code = "copper_dagger", Quantity = 1 },
            new DropSchema { Code = "sticky_sword", Quantity = 1 },
            new DropSchema { Code = "sticky_dagger", Quantity = 1 },
            new DropSchema { Code = "fire_staff", Quantity = 1 },
            new DropSchema { Code = "water_bow", Quantity = 1 },
            new DropSchema { Code = "copper_helmet", Quantity = 1 },
            new DropSchema { Code = "copper_armor", Quantity = 1 },
            new DropSchema { Code = "feather_coat", Quantity = 1 },
            new DropSchema { Code = "copper_legs_armor", Quantity = 1 },
            new DropSchema { Code = "copper_boots", Quantity = 1 },
            new DropSchema { Code = "life_amulet", Quantity = 1 },
        ];

        bankItemCache
            .GetBankItems(Arg.Any<PlayerCharacter>(), Arg.Any<bool>())
            .Returns(call => bankItems);

        var character = PlayerCharacterHelper.GetFighterCharacter(gameState, 5);

        var result = await EquipmentService.EnsureFightEquipment(character, gameState, bankItems);

        Assert.Null(result);
    }

    [Fact(
        DisplayName = "Should not pick any new items at level 20, because the level 20 ring upgrades are too small"
    )]
    public async Task EnsureFightEquipment_ShouldNotPickAnyItemAtLevel20()
    {
        var bankItemCache = Substitute.For<BankItemCache>(
            Substitute.For<AccountRequester>(
                Substitute.For<ApiRequester>("1234", false),
                "test_account_name",
                Substitute.For<ILogger>()
            )
        );

        GameState gameState = ServiceHelper.GetPopulatedGameState(bankItemCache);

        List<DropSchema> bankItems =
        [
            new DropSchema { Code = "steel_battleaxe", Quantity = 1 },
            new DropSchema { Code = "skull_staff", Quantity = 1 },
            new DropSchema { Code = "battlestaff", Quantity = 1 },
            new DropSchema { Code = "fire_staff", Quantity = 1 },
            new DropSchema { Code = "skeleton_armor", Quantity = 1 },
            new DropSchema { Code = "skeleton_pants", Quantity = 1 },
            new DropSchema { Code = "skeleton_helmet", Quantity = 1 },
            new DropSchema { Code = "fire_and_earth_amulet", Quantity = 1 },
            new DropSchema { Code = "life_amulet", Quantity = 1 },
            new DropSchema { Code = "iron_ring", Quantity = 2 },
            new DropSchema { Code = "steel_ring", Quantity = 2 },
            new DropSchema { Code = "iron_boots", Quantity = 1 },
            new DropSchema { Code = "novice_guide", Quantity = 1 },
            new DropSchema { Code = "lich_race_medal", Quantity = 1 },
            new DropSchema { Code = "burn_rune", Quantity = 1 },
            new DropSchema { Code = "steel_boots", Quantity = 1 },
            new DropSchema { Code = "magic_wizard_hat", Quantity = 1 },
            new DropSchema { Code = "tromatising_mask", Quantity = 1 },
        ];

        bankItemCache
            .GetBankItems(Arg.Any<PlayerCharacter>(), Arg.Any<bool>())
            .Returns(call => bankItems);

        var bankDetails = new BankDetails
        {
            Slots = 50,
            NextExpansionCost = 0,
            Gold = 5_000_000,
            Expansions = 5,
        };

        bankItemCache.GetBankDetails().Returns(call => bankDetails);

        var character = PlayerCharacterHelper.GetFighterCharacter(gameState, 20);

        gameState.Characters = [character];

        var result = await EquipmentService.EnsureFightEquipment(character, gameState, bankItems);

        Assert.Null(result);
    }
}
