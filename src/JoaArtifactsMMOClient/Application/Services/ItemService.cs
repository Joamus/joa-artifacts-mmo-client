using Application.ArtifactsApi.Schemas;
using Application.Character;
using Application.Jobs;
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

    public static bool CanUseItem(ItemSchema item, CharacterSchema playerSchema)
    {
        foreach (var condition in item.Conditions)
        {
            int playerLevelOfSkill = 0;
            switch (condition.Code)
            {
                case "level":
                    playerLevelOfSkill = playerSchema.Level;
                    break;
                case "mining_level":
                    playerLevelOfSkill = playerSchema.MiningLevel;
                    break;
                case "alchemy_level":
                    playerLevelOfSkill = playerSchema.AlchemyLevel;
                    break;
                case "fishing_level":
                    playerLevelOfSkill = playerSchema.FishingLevel;
                    break;
                case "woodcutting_level":
                    playerLevelOfSkill = playerSchema.WoodcuttingLevel;
                    break;
            }

            if (
                condition.Operator == ItemConditionOperator.Gt
                && playerLevelOfSkill < condition.Value
            )
            {
                return false;
            }
        }

        return true;
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

    public static List<DropSchema> GetFoodToCookFromInventoryList(
        PlayerCharacter character,
        GameState gameState,
        List<DropSchema> itemSource
    )
    {
        Dictionary<string, int> ingredientAmounts = new();
        Dictionary<string, ItemSchema> potentialFoodsToCookDict = new();

        foreach (var item in itemSource)
        {
            var foods = gameState.CraftingLookupDict.GetValueOrNull(item.Code);

            if (foods is not null)
            {
                ingredientAmounts.Add(item.Code, item.Quantity);

                foreach (var food in foods)
                {
                    if (character.Schema.CookingLevel >= food.Level && food.Subtype == "food")
                    {
                        potentialFoodsToCookDict.Add(food.Code, food);
                    }
                }
            }
        }

        List<ItemSchema> potentialFoodsToCook = [];

        foreach (var food in potentialFoodsToCookDict)
        {
            potentialFoodsToCook.Add(food.Value);
        }

        CalculationService.SortItemsBasedOnEffect(potentialFoodsToCook, "heal");

        List<DropSchema> foodsToCook = [];

        foreach (var food in potentialFoodsToCook)
        {
            bool hasAllIngredients = true;
            int? amountThatCanBeCooked = null;

            foreach (var ingredient in food.Craft!.Items)
            {
                int amountInInventory = ingredientAmounts.GetValueOrNull(ingredient.Code);

                if (amountInInventory == 0)
                {
                    continue;
                }

                // If we need more than we have, then we can just skip on to the next food
                if (amountInInventory < ingredient.Quantity)
                {
                    hasAllIngredients = false;
                    break;
                }

                // Integer division
                int amountThatCanBeCookedAccordingToThisIngredient =
                    amountInInventory / ingredient.Quantity;

                // The ingredient that we have the least of, is gonna be the lowest denominator, e.g if we have 10 eggs for a dish requiring 1 egg and 1 fish,
                // then if we have 5 fish then we can only make 5 of the dishes
                if (
                    amountThatCanBeCooked is null
                    || amountThatCanBeCooked > amountThatCanBeCookedAccordingToThisIngredient
                )
                {
                    amountThatCanBeCooked = amountThatCanBeCookedAccordingToThisIngredient;
                }
            }

            List<InventorySlot> ingredientsToSubtract = [];

            if (hasAllIngredients && amountThatCanBeCooked is not null && amountThatCanBeCooked > 0) // always should be
            {
                foreach (var ingredient in food.Craft!.Items)
                {
                    ingredientsToSubtract.Add(
                        new InventorySlot
                        {
                            Code = ingredient.Code,
                            Quantity = amountThatCanBeCooked.Value * ingredient.Quantity,
                        }
                    );
                }

                foreach (var ingredient in ingredientsToSubtract)
                {
                    if (ingredientAmounts.ContainsKey(ingredient.Code))
                    {
                        ingredientAmounts[ingredient.Code] -= ingredient.Quantity;
                    }
                }

                foodsToCook.Add(
                    new DropSchema { Code = food.Code, Quantity = amountThatCanBeCooked.Value }
                // new CraftItem(character, gameState, food.Code, amountThatCanBeCooked.Value)
                // new ObtainItem(character, gameState, food.Code, amountThatCanBeCooked.Value)
                );
            }
        }
        return foodsToCook;
    }
}
