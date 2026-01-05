using Application.ArtifactsApi.Schemas;
using Application.Character;
using Application.Errors;
using Application.Records;
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
}
