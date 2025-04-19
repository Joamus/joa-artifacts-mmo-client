using System.Reflection.Metadata.Ecma335;
using System.Security.Permissions;
using Application.ArtifactsApi.Schemas;
using Application.ArtifactsApi.Schemas.Responses;
using Application.Character;
using Application.Errors;
using Application.Jobs;
using Application.Services;
using Applicaton.Jobs;
using Microsoft.Extensions.ObjectPool;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public class GatherResource : CharacterJob
{
    private static readonly List<string> _allowedSubtypes =
    [
        "fishing",
        "mining",
        "alchemy",
        "woodcutting",
    ];

    protected int _amount { get; set; }

    protected int _progressAmount { get; set; } = 0;

    private bool _allowUsingInventory { get; init; } = false;

    public GatherResource(
        PlayerCharacter playerCharacter,
        string code,
        int amount,
        bool allowUsingInventory = false
    )
        : base(playerCharacter)
    {
        _code = code;
        _amount = amount;
        _allowUsingInventory = allowUsingInventory;
    }

    public override async Task<OneOf<JobError, None>> RunAsync()
    {
        // In case of resuming a task
        _shouldInterrupt = false;

        if (_allowUsingInventory)
        {
            int amountInInventory = _playerCharacter.GetItemFromInventory(_code)?.Quantity ?? 0;

            _progressAmount = amountInInventory;
        }

        _logger.LogInformation(
            $"{GetType().Name} run started - for {_playerCharacter._character.Name} - progress {_code} ({_progressAmount}/{_amount})"
        );

        while (_progressAmount < _amount)
        {
            if (DepositUnneededItems.ShouldInitDepositItems(_playerCharacter))
            {
                _playerCharacter.QueueJobsBefore(Id, [new DepositUnneededItems(_playerCharacter)]);
                return new None();
            }

            if (_shouldInterrupt)
            {
                return new None();
            }

            var result = await InnerJobAsync();

            switch (result.Value)
            {
                case JobError jobError:
                    return jobError;
                default:
                    // Just continue
                    break;
            }
        }

        _logger.LogInformation(
            $"{GetType().Name} completed for {_playerCharacter._character.Name} - progress {_code} ({_progressAmount}/{_amount})"
        );

        return new None();
    }

    protected async Task<OneOf<JobError, None>> InnerJobAsync()
    {
        _logger.LogInformation(
            $"GatherJob status for {_playerCharacter._character.Name} - gathering {_code} ({_progressAmount}/{_amount})"
        );

        var matchingItem = _gameState.Items.Find(item => item.Code == _code);

        if (matchingItem is null)
        {
            return new JobError($"Could not find item with code {_code} - could not gather it");
        }

        if (matchingItem.Type != "resource" || !_allowedSubtypes.Contains(matchingItem.Subtype))
        {
            return new JobError(
                $"Item with code: {_code} - type: {matchingItem.Type} - sub type: {matchingItem.Type} is not a gatherable resource"
            );
        }

        int characterSkillLevel = 0;

        switch (matchingItem.Subtype)
        {
            case "alchemy":
                characterSkillLevel = _playerCharacter._character.AlchemyLevel;
                break;
            case "fishing":
                characterSkillLevel = _playerCharacter._character.CookingLevel;
                break;
            case "mining":
                characterSkillLevel = _playerCharacter._character.MiningLevel;
                break;
            case "woodcutting":
                characterSkillLevel = _playerCharacter._character.WoodcuttingLevel;
                break;
        }

        if (matchingItem.Level > characterSkillLevel)
        {
            return new JobError(
                $"Could not gather item {_code} - current skill level is {characterSkillLevel}, required is {matchingItem.Level}",
                JobStatus.InsufficientSkill
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
                GatherResponse response = (GatherResponse)result.Value;
                _progressAmount +=
                    response.Data.Details.Items.Find(item => item.Code == _code)?.Quantity ?? 0;
                break;
        }
        return new None();
    }
}
