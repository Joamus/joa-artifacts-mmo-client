using Application;
using Application.ArtifactsApi.Schemas;
using Application.Character;
using Application.Services.ApiServices;
using Infrastructure;
using Moq;

namespace JoaArtifactsMMOClientTests.Helpers;

public static class PlayerCharacterHelper
{
    public static PlayerCharacter GetFighterCharacter(GameState? _gameState = null)
    {
        var apiRequester = ServiceHelper.GetTestApiRequester();

        GameState gameState = _gameState ?? ServiceHelper.GetEmptyGameState();

        CharacterSchema schema = new CharacterSchema
        {
            Name = "TestChar",
            Hp = 100,
            MaxHp = 100,
            X = 1,
            Y = 1,
            Layer = MapLayer.Overworld,
            MapId = 1,
            WeaponSlot = "",
            RuneSlot = "",
            ShieldSlot = "",
            HelmetSlot = "",
            BodyArmorSlot = "",
            LegArmorSlot = "",
            BootsSlot = "",
            Ring1Slot = "",
            Ring2Slot = "",
            AmuletSlot = "",
            Artifact1Slot = "",
            Artifact2Slot = "",
            Artifact3Slot = "",
            Utility1Slot = "",
            Utility1SlotQuantity = 0,
            Utility2Slot = "",
            Utility2SlotQuantity = 0,
            BagSlot = "",
            InventoryMaxItems = 100,
            Inventory = [],
        };

        var character = new PlayerCharacter(schema, gameState, apiRequester, null);
        return character;
    }
}
