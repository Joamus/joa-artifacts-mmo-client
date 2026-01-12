using Application.ArtifactsApi.Schemas;
using Application.Character;
using Application.Errors;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public class RecycleUnusedItems : CharacterJob
{
    public RecycleUnusedItems(PlayerCharacter character, GameState gameState)
        : base(character, gameState) { }

    protected override async Task<OneOf<AppError, None>> ExecuteAsync()
    {
        return new None();
    }

    // public async DropSchema? GetNextItemToRecycle()
    // {
    //     var bankItems = await gameState.BankItemCache.GetBankItems(Character);

    //     foreach (var item in bankItems.Data)
    // 	{
    // 		if (string.IsNullOrWhiteSpace(item.Code))
    // 		{
    // 			continue;
    // 		}
    // 		if (item.)
    // 	}
    // }
    //

    public static Dictionary<string, NpcSchema> GetActiveNpcs(GameState gameState)
    {
        Dictionary<string, NpcSchema> npcs = [];

        foreach (var npc in gameState.Npcs)
        {
            var npcEvent = gameState.EventService.EventEntitiesDict.GetValueOrNull(npc.Code);

            if (npcEvent is not null)
            {
                if (gameState.EventService.WhereIsEntityActive(npcEvent.Code) is null)
                {
                    continue;
                }
            }

            npcs.Add(npc.Code, npc);
        }

        return npcs;
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


    public static void GetAll() { }

    public static void Something() { }
}
