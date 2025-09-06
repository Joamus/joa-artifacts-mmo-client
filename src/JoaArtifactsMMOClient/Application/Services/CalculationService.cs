using Application.ArtifactsApi.Schemas;
using Application.Records;

namespace Application.Services;

public static class CalculationService
{
    public static int CalculateDistanceToMap(int originX, int originY, int mapX, int mapY)
    {
        return Math.Abs((mapX - originX) + mapY - originY);
    }

    public static void SortFoodBasedOnHealValue(
        List<ItemInInventory> foodItems,
        bool ascending = false
    )
    {
        foodItems.Sort(
            (a, b) =>
            {
                var aHealValue = a.Item.Effects.Find(effect => effect.Code == "heal")?.Value ?? 0;
                var bHealValue = b.Item.Effects.Find(effect => effect.Code == "heal")?.Value ?? 0;

                if (ascending)
                {
                    return aHealValue.CompareTo(bHealValue);
                }
                else
                {
                    return bHealValue.CompareTo(aHealValue);
                }
            }
        );
    }

    public static void SortFoodBasedOnHealValue(List<ItemSchema> foodItems, bool ascending = false)
    {
        foodItems.Sort(
            (a, b) =>
            {
                var aHealValue = a.Effects.Find(effect => effect.Code == "heal")?.Value ?? 0;
                var bHealValue = b.Effects.Find(effect => effect.Code == "heal")?.Value ?? 0;

                if (ascending)
                {
                    return aHealValue.CompareTo(bHealValue);
                }
                else
                {
                    return bHealValue.CompareTo(aHealValue);
                }
            }
        );
    }

    public static bool IsItemBetter(ItemSchema? a, ItemSchema b)
    {
        // TODO: IMPL
        if (a is null)
        {
            return true;
        }

        return false;
    }
}
