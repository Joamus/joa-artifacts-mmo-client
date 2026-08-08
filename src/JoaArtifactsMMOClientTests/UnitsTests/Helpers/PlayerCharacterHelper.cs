using Application;
using Application.ArtifactsApi.Schemas;
using Application.Character;

namespace JoaArtifactsMMOClientTests.Helpers;

public static class PlayerCharacterHelper
{
    public static PlayerCharacter GetFighterCharacter(GameState gameState, int level)
    {
        var apiRequester = ServiceHelper.GetTestApiRequester();

        CharacterSchema schema = new CharacterSchema
        {
            Name = $"TestChar_{Guid.NewGuid()}",
            Level = level,
            Hp = GetHpBasedOnLevel(level),
            MaxHp = GetHpBasedOnLevel(level),
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

        var character = new PlayerCharacter(
            schema,
            gameState,
            apiRequester,
            new CharacterConfig { }
        );
        return character;
    }

    public static int GetHpBasedOnLevel(int level)
    {
        return 100 + ((level - 1) * 5);
    }
}
