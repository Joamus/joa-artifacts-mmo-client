using Application;
using Application.Character;
using Application.Errors;
using Application.Jobs;
using OneOf;
using OneOf.Types;

/**
* This job should basically tidy up the bank and inventory of the character.
* This includes converting items in the bank into crafted items, e.g cooking food, etc, so we get enough deposit space.
* It should also include buying bank expansions if possible.

* If it really cannot tidy up enough, then it should salvage salvageable items, or destroy unneeded low lvl items.
* It should probably destroy low lvl items, according to some rules like:
* - The item is 5 levels below any of our characters, and if it's an equippable item, then it should not be an upgrade for anyone
* -- Should elaborate, and possibly take into account if the item is used to craft anything, and if what it's crafted into's level is still below the range.
* -- For example, the vampiric armor is for level 20, but crafts into a lvl 40 armor. Arguably, we could always just make a new one, but maybe it should be a last resort to get rid of it
* -- If it's food items, then we should just get rid of it if it's below the level range, no matter if cooked or not. Food is easy to come by.

* -- Items like golden eggs, or items that are not obtainable easily should be kept, for example Jasper Crystals etc - maybe?
*


* Another job should also be made for considering upgrades for the characters, e.g every time they level up, or have to fight, they should possibly evaluate which items they can now obtain, and how to get them.
* E.g a character is level 5, and now new armor is available to them
* - Look through the items, and judge which one is an upgrade. If an item is an upgrade then:
* -- See if it's in our inventory. If so, equip it.
* -- If it's in our bank, then go put it on.
* -- If another character has one in their inventory, maybe they should deposit it and we pick it up?
* -- Else, see if there is a character who can craft it for you. If they can, they should be given that task, and also deposit it in the bank when done. That should queue a job for us to go pick it up, and equip it
* --
*
*/
public class HouseKeeping : CharacterJob
{
    public HouseKeeping(PlayerCharacter playerCharacter, GameState gameState)
        : base(playerCharacter, gameState) { }

    protected override Task<OneOf<AppError, None>> ExecuteAsync()
    {
        throw new NotImplementedException();
    }

    public async Task RecycleItems() { }

    public async Task SellUnneededItems() { }
}
