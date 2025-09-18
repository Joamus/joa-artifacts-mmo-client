using System.Text.Json.Serialization;

namespace Application.ArtifactsApi.Schemas;

public record CharacterSchema
{
    public string Name { get; set; } = "";

    public int Level { get; set; }

    public int Xp { get; set; }

    public int MaxXp { get; set; }

    public int Gold { get; set; }

    public int Speed { get; set; }

    public int MiningLevel { get; set; }

    public int MiningXp { get; set; }

    public int MiningMaxXp { get; set; }

    public int WoodcuttingLevel { get; set; }

    public int WoodcuttingXp { get; set; }

    public int WoodcuttingMaxXp { get; set; }

    public int FishingLevel { get; set; }

    public int FishingXp { get; set; }

    public int FishingMaxXp { get; set; }

    public int WeaponcraftingLevel { get; set; }

    public int WeaponcraftingXp { get; set; }

    public int WeaponcraftingMaxXp { get; set; }

    public int GearcraftingLevel { get; set; }

    public int GearcraftingXp { get; set; }

    public int GearcraftingMaxXp { get; set; }

    public int CookingLevel { get; set; }

    public int CookingXp { get; set; }

    public int CookingMaxXp { get; set; }

    public int AlchemyLevel { get; set; }

    public int AlchemyXp { get; set; }

    public int AlchemyMaxXp { get; set; }
    public int JewelrycraftingLevel { get; set; }

    public int JewelrycraftingXp { get; set; }

    public int JewelrycraftingMaxXp { get; set; }

    public int Hp { get; set; }

    public int MaxHp { get; set; }

    public int Haste { get; set; }

    public int CriticalStrike { get; set; }

    public int Wisdom { get; set; }

    public int Prospecting { get; set; }

    public int AttackFire { get; set; }

    public int AttackEarth { get; set; }

    public int AttackWater { get; set; }

    public int AttackAir { get; set; }

    public int Dmg { get; set; }

    public int DmgFire { get; set; }

    public int DmgEarth { get; set; }

    public int DmgWater { get; set; }

    public int DmgAir { get; set; }
    public int ResFire { get; set; }

    public int ResEarth { get; set; }

    public int ResWater { get; set; }

    public int ResAir { get; set; }

    public int X { get; set; }

    public int Y { get; set; }

    public int Cooldown { get; set; }

    public DateTime CooldownExpiration { get; set; }

    public string? WeaponSlot { get; set; }

    public string? RuneSlot { get; set; }

    public string ShieldSlot { get; set; } = "";

    public string HelmetSlot { get; set; } = "";

    public string BodyArmorSlot { get; set; } = "";

    public string LegArmorSlot { get; set; } = "";

    public string BootsSlot { get; set; } = "";

    public string Ring1Slot { get; set; } = "";

    public string Ring2Slot { get; set; } = "";

    public string AmuletSlot { get; set; } = "";

    public string Artifact1Slot { get; set; } = "";

    public string Artifact2Slot { get; set; } = "";

    public string Artifact3Slot { get; set; } = "";

    public string Utility1Slot { get; set; } = "";

    public int Utility1SlotQuantity { get; set; }

    public string Utility2Slot { get; set; } = "";

    public int Utility2SlotQuantity { get; set; }

    public string BagSlot { get; set; } = "";

    public string Task { get; set; } = "";

    // Can be "monsters", probably "items" also
    public string TaskType { get; set; } = "";
    public int TaskProgress { get; set; }
    public int TaskTotal { get; set; }
    public int InventoryMaxItems { get; set; }
    public List<InventorySlot> Inventory { get; set; } = [];
}
