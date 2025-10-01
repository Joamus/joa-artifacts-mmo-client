using Application.ArtifactsApi.Schemas;
using Microsoft.Extensions.ObjectPool;
using OneOf.Types;

namespace Application.Services;

public static class ItemService
{
    public static List<ItemSchema> CraftsInto(List<ItemSchema> items, ItemSchema ingredientItem)
    {
        List<ItemSchema> crafts = [];

        foreach (var item in items)
        {
            if (item.Craft is null)
            {
                continue;
            }

            DropSchema? ingredient = item.Craft.Items.FirstOrDefault(_item =>
                _item.Code == ingredientItem.Code
            );

            if (ingredient is not null)
            {
                crafts.Add(item);
            }
        }

        return crafts;
    }

    public static bool IsEquipment(string itemType)
    {
        return new[]
        {
            "consumable",
            "weapon",
            "helmet",
            "body_armor",
            "leg_armor",
            "ring",
            "amulet",
            "artifact",
            "rune",
            "bag",
            "utility",
        }.Contains(itemType);
    }

    public static bool CanUseItem(ItemSchema item, int playerLevel)
    {
        var levelCondition = item.Conditions.Find(condition => condition.Code == "level");

        return levelCondition is null || levelCondition.Value <= playerLevel;
    }

    public static string[] GetItemSlotsFromItemType(string itemType)
    {
        string[] itemSlots = [];

        switch (itemType)
        {
            case "weapon":
                itemSlots = ["WeaponSlot"];
                break;
            case "rune":
                itemSlots = ["RuneSlot"];
                break;
            case "shield":
                itemSlots = ["ShieldSlot"];
                break;
            case "helmet":
                itemSlots = ["HelmetSlot"];
                break;
            case "body_armor":
                itemSlots = ["BodyArmorSlot"];
                break;
            case "leg_armor":
                itemSlots = ["LegArmorSlot"];
                break;
            case "boots":
                itemSlots = ["BootsSlot"];
                break;
            case "ring":
                itemSlots = ["Ring1Slot", "Ring2Slot"];
                break;
            case "amulet":
                itemSlots = ["AmuletSlot"];
                break;
            case "artifact":
                itemSlots = ["Artifact1Slot", "Artifact2Slot", "Artifact3Slot"];
                break;
            case "utility":
                itemSlots = ["Utility1Slot"];
                break;
            case "bag":
                itemSlots = ["BagSlot"];
                break;
        }

        return itemSlots;
    }

    public static bool IsHealthPotion(ItemSchema item)
    {
        return item.Subtype == "potion"
            && item.Effects.Find(effect => effect.Code == "restore") is not null;
    }

    public static int GetEffect(ItemSchema item, string key)
    {
        return item.Effects.Find(effect => effect.Code == key)?.Value ?? 0;
    }
}
