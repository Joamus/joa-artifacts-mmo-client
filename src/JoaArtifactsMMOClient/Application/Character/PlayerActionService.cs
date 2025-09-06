using System.Security.Permissions;
using Application;
using Application.ArtifactsApi.Schemas;
using Application.Character;
using Application.Errors;
using OneOf;
using OneOf.Types;

public class PlayerActionService
{
    private static readonly int MAX_AMOUNT_UTILITY_SLOT = 100;
    readonly GameState _gameState;

    PlayerActionService(GameState gameState)
    {
        _gameState = gameState;
    }

    async Task<OneOf<AppError, None>> EquipItem(
        PlayerCharacter playerCharacter,
        string code,
        int amount
    )
    {
        var item = playerCharacter.GetItemFromInventory(code);

        if (item is null)
        {
            return new AppError(
                $"Item not found in inventory with code {code}",
                ErrorStatus.NotFound
            );
        }

        var matchingItem = _gameState.ItemsDict.ContainsKey(code)
            ? _gameState.ItemsDict[code]
            : null;

        if (matchingItem is null)
        {
            return new AppError($"Item not found with code {code}", ErrorStatus.NotFound);
        }

        bool isUtility = matchingItem.Type == "utility";

        OneOf<InventorySlot, AppError>? itemSlot = null;
        string slot = item.Slot;

        // We need to handle that we might have x potions already in a slot, so we should fill it up, and then equip more in another slot - we can equip up to 100 per slot
        if (isUtility)
        {
            var utility1Slot = playerCharacter.GetItemSlot("Utility1Slot");

            switch (utility1Slot.Value)
            {
                case InventorySlot inventorySlot:
                    if (inventorySlot.Code == "")
                    {
                        slot = "Utility2Slot";
                    }
                    else
                    {
                        itemSlot = utility1Slot;
                        slot = "Utility1Slot";
                    }
                    break;
                case AppError appError:
                    return appError;
            }
        }

        if (itemSlot is null)
        {
            itemSlot = playerCharacter.GetItemSlot(slot);
        }

        switch (itemSlot!.Value.Value)
        {
            case InventorySlot inventorySlot:
                if (inventorySlot.Code != "")
                {
                    // Trying to equip the same item - at the moment we don't allow using both utility slots for same item
                    if (inventorySlot.Code == code && isUtility)
                    {
                        var amountThatCanBeAdded = MAX_AMOUNT_UTILITY_SLOT - inventorySlot.Quantity;

                        int amountToEquip = Math.Min(amountThatCanBeAdded, amount);

                        await playerCharacter.EquipItem(code, inventorySlot.Slot, amountToEquip);

                        return new None();
                    }

                    await playerCharacter.UnequipItem(inventorySlot.Slot, inventorySlot.Quantity);
                }

                await playerCharacter.EquipItem(code, inventorySlot.Slot, amount);

                break;
            case AppError appError:
                return appError;
        }

        return new None();
    }
}
