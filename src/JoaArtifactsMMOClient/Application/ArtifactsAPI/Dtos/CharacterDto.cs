using System.Text.Json.Serialization;
using Applcation.ArtifactsAPI.Dtos;

public record CharacterDto
{
    [JsonPropertyName("name")]
    string Name = "";

    [JsonPropertyName("level")]
    int Level;

    [JsonPropertyName("xp")]
    int Xp;

    [JsonPropertyName("maxxp")]
    int MaxXp;

    [JsonPropertyName("gold")]
    int Gold;

    [JsonPropertyName("speed")]
    int Speed;

    [JsonPropertyName("mininglevel")]
    int MiningLevel;

    [JsonPropertyName("miningxp")]
    int MiningXp;

    [JsonPropertyName("miningmaxxp")]
    int MiningMaxXp;

    [JsonPropertyName("woodcuttinglevel")]
    int WoodcuttingLevel;

    [JsonPropertyName("woodcuttingxp")]
    int WoodcuttingXp;

    [JsonPropertyName("woodcuttingmaxxp")]
    int WoodcuttingMaxXp;

    [JsonPropertyName("fishinglevel")]
    int FishingLevel;

    [JsonPropertyName("fishingxp")]
    int FishingXp;

    [JsonPropertyName("fishingmaxxp")]
    int FishingMaxXp;

    [JsonPropertyName("weaponcraftinglevel")]
    int WeaponCraftingLevel;

    [JsonPropertyName("weaponcraftingxp")]
    int WeaponCraftingXp;

    [JsonPropertyName("weaponcraftingmaxxp")]
    int WeaponCraftingMaxXp;

    [JsonPropertyName("gearcraftinglevel")]
    int GearcraftingLevel;

    [JsonPropertyName("gearcraftingxp")]
    int GearcraftingXp;

    [JsonPropertyName("gearcraftingmaxxp")]
    int GearcraftingMaxXp;

    [JsonPropertyName("cookinglevel")]
    int CookingLevel;

    [JsonPropertyName("cookingxp")]
    int CookingXp;

    [JsonPropertyName("cookingmaxxp")]
    int CookingMaxXp;

    [JsonPropertyName("alchemylevel")]
    int AlchemyLevel;

    [JsonPropertyName("alchemyxp")]
    int AlchemyXp;

    [JsonPropertyName("alchemymaxxp")]
    int AlchemyMaxXp;

    [JsonPropertyName("hp")]
    int Hp;

    [JsonPropertyName("maxhp")]
    int MaxHp;

    [JsonPropertyName("haste")]
    int Haste;

    [JsonPropertyName("criticalstrike")]
    int CriticalStrike;

    [JsonPropertyName("wisdom")]
    int Wisdom;

    [JsonPropertyName("prospecting")]
    int Prospecting;

    [JsonPropertyName("attackfire")]
    int AttackFire;

    [JsonPropertyName("attackearth")]
    int AttackEarth;

    [JsonPropertyName("attackwater")]
    int AttackWater;

    [JsonPropertyName("attackair")]
    int AttackAir;

    [JsonPropertyName("dmg")]
    int Dmg;

    [JsonPropertyName("dmgfire")]
    int DmgFire;

    [JsonPropertyName("dmgearth")]
    int DmgEarth;

    [JsonPropertyName("dmgwater")]
    int DmgWater;

    [JsonPropertyName("dmgair")]
    int DmgAir;

    [JsonPropertyName("resfire")]
    int ResFire;

    [JsonPropertyName("researth")]
    int ResEarth;

    [JsonPropertyName("reswater")]
    int ResWater;

    [JsonPropertyName("resair")]
    int ResAir;

    [JsonPropertyName("x")]
    int X;

    [JsonPropertyName("y")]
    int Y;

    [JsonPropertyName("cooldown")]
    int Cooldown;

    [JsonPropertyName("cooldown_expiration")]
    DateTime CooldownExpiration;

    [JsonPropertyName("weapon_slot")]
    string WeaponSlot = "";

    [JsonPropertyName("rune_slot")]
    string RuneSlot = "";

    [JsonPropertyName("shield_slot")]
    string ShieldSlot = "";

    [JsonPropertyName("helmet_slot")]
    string HelmetSlot = "";

    [JsonPropertyName("body_armor_slot")]
    string BodyArmorSlot = "";

    [JsonPropertyName("leg_armor_slot")]
    string LegArmorSlot = "";

    [JsonPropertyName("boots_slot")]
    string BootsSlot = "";

    [JsonPropertyName("ring1_slot")]
    string Ring1Slot = "";

    [JsonPropertyName("ring2_slot")]
    string Ring2Slot = "";

    [JsonPropertyName("amulet_slot")]
    string AmuletSlot = "";

    [JsonPropertyName("artifact1_slot")]
    string Artifact1Slot = "";

    [JsonPropertyName("artifact2_slot")]
    string Artifact2Slot = "";

    [JsonPropertyName("artifact3_slot")]
    string Artifact3Slot = "";

    [JsonPropertyName("utility1_slot")]
    string Utility1Slot = "";

    [JsonPropertyName("utility1_slot_quantity")]
    int Utility1SlotQuantity;

    [JsonPropertyName("utility2_slot")]
    int Utility2Slot;

    [JsonPropertyName("utility2_slot_quantity")]
    int Utility2SlotQuantity;

    [JsonPropertyName("bag_slot")]
    string BagSlot = "";

    [JsonPropertyName("task")]
    string Task = "";

    [JsonPropertyName("task_type")]
    string TaskType = "";

    [JsonPropertyName("task_progress")]
    int TaskProgress;

    [JsonPropertyName("task_total")]
    int TaskTotal;

    [JsonPropertyName("inventory_max_items")]
    int InventoryMaxItems;

    [JsonPropertyName("inventory")]
    List<InventoryItemDto> inventory = [];
}
