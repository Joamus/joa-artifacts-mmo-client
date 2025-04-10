using System.Reflection.Metadata.Ecma335;
using System.Security.Permissions;
using Application.ArtifactsApi.Schemas;
using Application.ArtifactsApi.Schemas.Responses;
using Application.Character;
using Application.Jobs;
using Microsoft.Extensions.ObjectPool;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public class GatherJob : CharacterJob
{
    private static readonly List<string> allowedSubtypes =
    [
        "fishing",
        "mining",
        "alchemy",
        "woodcutting",
    ];

    public GatherJob(PlayerCharacter character, string code, int amount, GameState gameState)
        : base(character, code, amount, gameState) { }

    public override async Task<OneOf<JobError, None>> RunAsync()
    {
        _logger.LogInformation(
            $"GatherJob started for {_playerCharacter._character.Name} - gathering ${_code} (${_progressAmount}/${_amount})"
        );
        // We already have x amount of the item, no reason to gather more.
        // if (_playerCharacter.GetItemFromInventory(_code)?.Quantity >= _amount)
        // {
        //     return new None();
        // }

        var matchingItem = _gameState._items.Find(item => item.Code == _code);

        if (matchingItem is null)
        {
            return new JobError($"Could not find item with code {_code} - could not gather it");
        }

        if (matchingItem.Type != "resource" || !allowedSubtypes.Contains(matchingItem.Subtype))
        {
            return new JobError(
                $"Item with code: {_code} - type: {matchingItem.Type} - sub type: {matchingItem.Type} is not a gatherable resource"
            );
        }

        await _playerCharacter.NavigateTo(_code, ContentType.Resource);

        var result = await _playerCharacter.Gather();

        switch (result.Value)
        {
            case JobError jobError:
            {
                return jobError;
            }
            case GatherResponse:
                // _progressAmount =
                //     _playerCharacter
                //         ._character.Inventory.FirstOrDefault(item => item.Code == _code)
                //         ?.Quantity ?? 0;
                GatherResponse response = (GatherResponse)result.Value;
                _progressAmount +=
                    response.Data.Details.Items.Find(item => item.Code == _code)?.Quantity ?? 0;

                if (_amount >= _progressAmount)
                {
                    _logger.LogInformation(
                        $"GatherJob completed for {_playerCharacter._character.Name} - gathered ${_code} (${_progressAmount}/${_amount})"
                    );
                    return new None();
                }
                else
                {
                    return await RunAsync();
                }
            default:
                return new None();
        }
    }
}
