using Application.ArtifactsApi.Schemas;
using Application.Character;
using Application.Errors;
using Application.Records;
using Application.Services;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public class CookEverythingInInventory : CharacterJob
{
    public CookEverythingInInventory(PlayerCharacter playerCharacter, GameState gameState)
        : base(playerCharacter, gameState) { }

    protected override Task<OneOf<AppError, None>> ExecuteAsync()
    {
        Dictionary<string, int> ingredientAmounts = new();
        Dictionary<string, ItemSchema> potentialFoodsToCook = new();

        foreach (var item in Character.Schema.Inventory)
        {
            bool isIngredient = gameState.CraftingLookupDict.ContainsKey(item.Code);

            if (isIngredient)
            {
                ingredientAmounts.Add(item.Code, item.Quantity);

                var foods = gameState.CraftingLookupDict[item.Code];

                foreach (var food in foods)
                {
                    if (Character.Schema.CookingLevel >= food.Level && food.Subtype == "food")
                    {
                        potentialFoodsToCook.Add(food.Code, food);
                    }
                }
            }
        }

        List<ItemSchema> foodsToCook = [];

        foreach (var food in potentialFoodsToCook)
        {
            foodsToCook.Add(food.Value);
        }

        CalculationService.SortItemsBasedOnEffect(foodsToCook, "heal");

        List<CharacterJob> jobs = [];

        foreach (var food in foodsToCook)
        {
            bool hasAllIngredients = true;
            int? amountThatCanBeCooked = null;

            foreach (var ingredient in food.Craft!.Items)
            {
                int amountInInventory = ingredientAmounts[ingredient.Code];

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
                    ingredientAmounts[ingredient.Code] -= ingredient.Quantity;
                }

                jobs.Add(
                    new CraftItem(Character, gameState, food.Code, amountThatCanBeCooked.Value)
                );
            }
        }

        if (jobs.Count > 0)
        {
            Character.QueueJobsAfter(Id, jobs);
        }

        return Task.FromResult<OneOf<AppError, None>>(new None());
    }
}
