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

    protected int Amount { get; set; }

    protected int ProgressAmount { get; set; } = 0;

    private bool _allowUsingInventory { get; init; }

    public GatherResource(
        PlayerCharacter playerCharacter,
        GameState gameState,
        string code,
        int amount,
        bool allowUsingInventory = false
    )
        : base(playerCharacter, gameState)
    {
        Code = code;
        Amount = amount;
        _allowUsingInventory = allowUsingInventory;
    }

    public override async Task<OneOf<AppError, None>> RunAsync()
    {
        // In case of resuming a task
        _shouldInterrupt = false;

        if (_allowUsingInventory)
        {
            int amountInInventory = _playerCharacter.GetItemFromInventory(Code)?.Quantity ?? 0;

            ProgressAmount = amountInInventory;
        }

        _logger.LogInformation(
            $"{GetType().Name} run started - for {_playerCharacter.Character.Name} - progress {Code} ({ProgressAmount}/{Amount})"
        );

        while (ProgressAmount < Amount)
        {
            if (DepositUnneededItems.ShouldInitDepositItems(_playerCharacter))
            {
                _playerCharacter.QueueJobsBefore(
                    Id,
                    [new DepositUnneededItems(_playerCharacter, _gameState)]
                );
                return new None();
            }

            if (_shouldInterrupt)
            {
                return new None();
            }

            var result = await InnerJobAsync();

            switch (result.Value)
            {
                case AppError jobError:
                    return jobError;
                default:
                    // Just continue
                    break;
            }
        }

        _logger.LogInformation(
            $"{GetType().Name} completed for {_playerCharacter.Character.Name} - progress {Code} ({ProgressAmount}/{Amount})"
        );

        return new None();
    }

    protected async Task<OneOf<AppError, None>> InnerJobAsync()
    {
        _logger.LogInformation(
            $"GatherJob status for {_playerCharacter.Character.Name} - gathering {Code} ({ProgressAmount}/{Amount})"
        );

        var matchingItem = _gameState.Items.Find(item => item.Code == Code);

        if (matchingItem is null)
        {
            return new AppError($"Could not find item with code {Code} - could not gather it");
        }

        if (matchingItem.Type != "resource" || !_allowedSubtypes.Contains(matchingItem.Subtype))
        {
            return new AppError(
                $"Item with code: {Code} - type: {matchingItem.Type} - sub type: {matchingItem.Type} is not a gatherable resource"
            );
        }

        int characterSkillLevel = 0;

        switch (matchingItem.Subtype)
        {
            case "alchemy":
                characterSkillLevel = _playerCharacter.Character.AlchemyLevel;
                break;
            case "fishing":
                characterSkillLevel = _playerCharacter.Character.CookingLevel;
                break;
            case "mining":
                characterSkillLevel = _playerCharacter.Character.MiningLevel;
                break;
            case "woodcutting":
                characterSkillLevel = _playerCharacter.Character.WoodcuttingLevel;
                break;
        }

        if (matchingItem.Level > characterSkillLevel)
        {
            return new AppError(
                $"Could not gather item {Code} - current skill level is {characterSkillLevel}, required is {matchingItem.Level}",
                ErrorStatus.InsufficientSkill
            );
        }

        await _playerCharacter.NavigateTo(Code, ContentType.Resource);

        var result = await _playerCharacter.Gather();

        switch (result.Value)
        {
            case AppError jobError:
            {
                return jobError;
            }
            case GatherResponse:
                GatherResponse response = (GatherResponse)result.Value;
                ProgressAmount +=
                    response.Data.Details.Items.Find(item => item.Code == Code)?.Quantity ?? 0;
                break;
        }
        return new None();
    }
}
