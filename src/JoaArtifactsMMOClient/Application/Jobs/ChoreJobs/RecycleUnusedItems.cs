using System.Text.RegularExpressions;
using Application.Artifacts.Schemas;
using Application.ArtifactsApi.Schemas;
using Application.Character;
using Application.Errors;
using Application.Records;
using Application.Services;
using Applicaton.Jobs;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public class RecycleUnusedItems : CharacterJob
{
    public const int RECYCLE_LEVEL_DIFF = 15;

    public RecycleUnusedItems(PlayerCharacter character, GameState gameState)
        : base(character, gameState) { }

    protected override async Task<OneOf<AppError, None>> ExecuteAsync()
    {
        logger.LogInformation($"{JobName}: [{Character.Schema.Name}] run started");

        List<DropSchema> items = await GetRecycleableItemsFromBank();

        if (items.Count == 0)
        {
            return new None();
        }

        logger.LogInformation(
            $"{JobName}: [{Character.Schema.Name}] running - found {items.Count} different items to deposit"
        );

        // Just deposit everything, will give more room for recycling
        await Character.PlayerActionService.DepositAllItems();

        Dictionary<Skill, List<DropSchema>> skillToItemsDict = [];

        foreach (var item in items)
        {
            var matchingItem = gameState.ItemsDict.GetValueOrNull(item.Code)!;
            Skill skill = matchingItem.Craft!.Skill;

            if (skillToItemsDict.GetValueOrDefault(skill) is null)
            {
                skillToItemsDict.Add(skill, []);
            }

            skillToItemsDict.GetValueOrDefault(skill)!.Add(item);
        }

        List<CharacterJob> jobs = [];

        foreach (var skill in skillToItemsDict)
        {
            foreach (var item in skill.Value)
            {
                var matchingItem = gameState.ItemsDict.GetValueOrNull(item.Code)!;

                // Iterations like this is good enough for now, we could be more accurate
                List<int> iterations = ObtainItem.CalculateObtainItemIterations(
                    matchingItem,
                    Character,
                    item.Quantity
                );

                foreach (var iteration in iterations)
                {
                    jobs.Add(new WithdrawItem(Character, gameState, item.Code, iteration));
                    jobs.Add(new RecycleItem(Character, gameState, item.Code, iteration));
                }
            }
        }

        logger.LogInformation(
            $"{JobName}: [{Character.Schema.Name}] queued {jobs.Count} jobs for recycling items"
        );

        await Character.QueueJobsAfter(Id, jobs);

        return new None();
    }

    /** Maybe the function gets the list of all items to run recycle jobs, sorted by the NPC they have to go to?
    ** Then we could basically just run a loop over a each item that:
    ** - Withdraw as many as appropriate. This should take into account that when recycling an item, we get more mats back than the items
    **   Lets be conservative, so we keep a buffer of at least the amount of ingredients per item, assuming we recycle 1 item at a time.
    **   e.g. if we recycle copper boots that is made of 6 copper bars, we should have at least 6 inventory slots, maybe 1 also for the boots itself.
    **   we probably want more space than that, else we run back and forth a lot.
    **   so we could try to take big batches, recycle one by one, and then deposit it all, and take another batch.
    **   we should look in the bank again, just in case the items are gone.
    **
    **  before we take the items, we also need to evaluate whether we should leave some in the bank.
    **  This calculation could maybe be simple, but we want to ensure that the item is no longer relevant to keep:
    **  - look at the lowest level character, and ensure the item is lower level.
    **  - somehow ensure that this item is not relevant for that character. Maybe ensure the item is at least 10 lvls lower,
    **    and that there are items of the same kind in the bank (or on the character) which are higher lvl for that character?
    **  - If we arent entirely sure that the item is totally redundant, we just recycle down to max 5, max 10 for rings (so everyone can use one)
    */


    public async Task<List<DropSchema>> GetRecycleableItemsFromBank()
    {
        var bankItems = await gameState.BankItemCache.GetBankItems(Character);

        List<DropSchema> itemsToRecycle = [];

        int lowestCharacterLevel = GetLowestCharacterLevel();

        int amountOfCharacters = gameState.Characters.Count;

        Dictionary<string, List<ItemSchema>> toolsByEffect = [];

        foreach (var item in gameState.Items)
        {
            if (item.Subtype != "tool")
            {
                continue;
            }

            var gatheringEffect = EffectService.GetSkillEffectFromItem(item);

            if (toolsByEffect.GetValueOrNull(gatheringEffect!.Code) is null)
            {
                toolsByEffect.Add(gatheringEffect.Code, []);
            }

            toolsByEffect[gatheringEffect.Code].Add(item);
        }

        foreach (var item in bankItems.Data)
        {
            var matchingItem = gameState.ItemsDict[item.Code];

            if (!IsItemRecycleable(matchingItem))
            {
                continue;
            }

            int amountToKeep = Math.Min(
                GetItemAmonutMinimumNeeded(matchingItem) * amountOfCharacters,
                item.Quantity
            );

            int amountToRecycle = item.Quantity - amountToKeep;

            int amountWithBetter = 0;

            // Check each character, and see if they have a better bag equipped
            // We should have some logic, where we count how many we need depending on all of the characters.

            if (matchingItem.Type == "bag")
            {
                amountWithBetter = gameState.Characters.Count(character =>
                {
                    var bag = gameState.ItemsDict.GetValueOrNull(character.Schema.BagSlot);

                    return bag?.Level >= matchingItem.Level;
                });
            }
            // Check each character, and see if they have better tools equipped
            else if (matchingItem.Subtype == "tool")
            {
                var toolEffect = EffectService.GetSkillEffectFromItem(matchingItem)!;

                int amountWithBetterOrSameTool = gameState.Characters.Count(character =>
                {
                    List<ItemInInventory> itemsInInventory =
                        character.GetItemsFromInventoryWithSubtype("tool");

                    if (!string.IsNullOrWhiteSpace(character.Schema.WeaponSlot))
                    {
                        ItemSchema equippedWeapon = gameState.ItemsDict.GetValueOrNull(
                            character.Schema.WeaponSlot
                        )!;

                        if (equippedWeapon.Subtype == "tool")
                        {
                            itemsInInventory.Add(
                                new ItemInInventory { Item = equippedWeapon, Quantity = 1 }
                            );
                        }
                    }

                    foreach (var tool in itemsInInventory)
                    {
                        var toolEffectInventoryItem = EffectService.GetSkillEffectFromItem(
                            tool.Item
                        );

                        // With tools, the lower effect the better, e.g. -30 is better than -10.
                        if (
                            toolEffectInventoryItem?.Code == toolEffect.Code
                            && toolEffectInventoryItem.Value <= toolEffect.Value
                        )
                        {
                            return true;
                        }
                    }

                    return false;
                });
                // Check characters' inventory/weapon slot, and see if they have a better tool
            }
            else
            {
                if (lowestCharacterLevel > matchingItem.Level + RECYCLE_LEVEL_DIFF)
                {
                    // TODO: We should recycle all of them, they are probably useless - make this better, and do fight sims at some point
                    amountToRecycle = item.Quantity;
                }
            }

            // We can recycle more in the bank, if the characters has a better item
            amountToRecycle += amountWithBetter;

            if (amountToRecycle > 0)
            {
                itemsToRecycle.Add(new DropSchema { Code = item.Code, Quantity = amountToRecycle });
            }
        }

        return itemsToRecycle;
    }

    public static bool IsItemRecycleable(ItemSchema item)
    {
        return item.Craft is not null && ItemService.RecycableItemTypes.Contains(item.Type);
    }

    public int GetLowestCharacterLevel()
    {
        int? lowestLevel = null;

        foreach (var character in gameState.Characters)
        {
            if (lowestLevel is null || character.Schema.Level < lowestLevel)
            {
                lowestLevel = character.Schema.Level;
            }
        }

        return lowestLevel ?? 1;
    }

    public int GetItemAmonutMinimumNeeded(ItemSchema item)
    {
        if (item.Type == "ring")
        {
            return 2;
        }

        return 1;
    }
}
