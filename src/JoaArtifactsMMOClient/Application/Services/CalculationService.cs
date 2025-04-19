using Application.ArtifactsApi.Schemas;
using Application.Records;

namespace Application.Services;

public static class CalculationService
{
    public static int CalculateDistanceToMap(int originX, int originY, int mapX, int mapY)
    {
        return Math.Abs((mapX - originX) + mapY - originY);
    }

    public static void SortFoodBasedOnHealValue(List<ItemInInventory> foodItems)
    {
        foodItems.Sort(
            (a, b) =>
            {
                var aHealValue = a.Item.Effects.Find(effect => effect.Code == "heal")?.Value ?? 0;
                var bHealValue = b.Item.Effects.Find(effect => effect.Code == "heal")?.Value ?? 0;

                return bHealValue.CompareTo(aHealValue);
            }
        );
    }

    public static void SortFoodBasedOnHealValue(List<ItemSchema> foodItems)
    {
        foodItems.Sort(
            (a, b) =>
            {
                var aHealValue = a.Effects.Find(effect => effect.Code == "heal")?.Value ?? 0;
                var bHealValue = b.Effects.Find(effect => effect.Code == "heal")?.Value ?? 0;

                return bHealValue.CompareTo(aHealValue);
            }
        );
    }
}
