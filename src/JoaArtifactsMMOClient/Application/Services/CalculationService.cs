using Application.ArtifactsApi.Schemas;
using Application.Records;

namespace Application.Services;

public static class CalculationService
{
    public static int CalculateDistanceToMap(int originX, int originY, int mapX, int mapY)
    {
        int xDiff = Math.Max(mapX, originX) - Math.Min(mapX, originX);
        int yDiff = Math.Max(mapY, originY) - Math.Min(mapY, originY);
        return Math.Abs(xDiff - yDiff);
    }

    public static void SortItemsBasedOnEffect(
        List<ItemSchema> items,
        string effectName,
        bool ascending = false
    )
    {
        items.Sort(
            (a, b) =>
            {
                var aHealValue = a.Effects.Find(effect => effect.Code == effectName)?.Value ?? 0;

                var bHealValue = b.Effects.Find(effect => effect.Code == effectName)?.Value ?? 0;

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

    public static void SortItemsBasedOnEffect(
        List<ItemInInventory> items,
        string effectName,
        bool ascending = false
    )
    {
        items.Sort(
            (a, b) =>
            {
                var aHealValue =
                    a.Item.Effects.Find(effect => effect.Code == effectName)?.Value ?? 0;

                var bHealValue =
                    b.Item.Effects.Find(effect => effect.Code == effectName)?.Value ?? 0;

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
