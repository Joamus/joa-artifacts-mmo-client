using Application.ArtifactsApi.Schemas;
using Microsoft.Extensions.ObjectPool;

namespace Application.Services;

public static class ItemLookupService
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
}
