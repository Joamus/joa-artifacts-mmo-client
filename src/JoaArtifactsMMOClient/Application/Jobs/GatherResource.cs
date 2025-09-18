using System.Reflection.Metadata.Ecma335;
using System.Security.Permissions;
using Application.ArtifactsApi.Schemas;
using Application.ArtifactsApi.Schemas.Responses;
using Application.Character;
using Application.Errors;
using Application.Jobs;
using Application.Services;
using Applicaton.Jobs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.ObjectPool;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public class GatherResourceItem : CharacterJob
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

    private bool AllowUsingInventory { get; init; }

    public bool CanTriggerTraining { get; set; }

    public GatherResourceItem(
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
        AllowUsingInventory = allowUsingInventory;
    }

    protected override async Task<OneOf<AppError, None>> ExecuteAsync()
    {
        logger.LogInformation(
            $"{GetType().Name}: [{Character.Schema.Name}] run started - progress {Code} ({ProgressAmount}/{Amount})"
        );

        // In case of resuming a task
        ShouldInterrupt = false;

        if (AllowUsingInventory)
        {
            int amountInInventory = Character.GetItemFromInventory(Code)?.Quantity ?? 0;

            ProgressAmount = amountInInventory;

            logger.LogInformation(
                $"{GetType().Name}: [{Character.Schema.Name}] found {amountInInventory} in inventory - progress {Code} ({ProgressAmount}/{Amount})"
            );
        }

        while (ProgressAmount < Amount)
        {
            if (DepositUnneededItems.ShouldInitDepositItems(Character))
            {
                Character.QueueJobsBefore(Id, [new DepositUnneededItems(Character, gameState)]);
                return new None();
            }

            if (ShouldInterrupt)
            {
                return new None();
            }

            var matchingItem = gameState.Items.Find(item => item.Code == Code);

            if (matchingItem is null)
            {
                return new AppError($"Could not find item with code {Code} - could not gather it");
            }

            await Character.PlayerActionService.EquipBestGatheringEquipment(matchingItem.Subtype);

            var result = await InnerJobAsync(matchingItem);

            switch (result.Value)
            {
                case AppError jobError:
                    return jobError;
                default:
                    // Just continue
                    break;
            }
        }

        logger.LogInformation(
            $"{GetType().Name}: [{Character.Schema.Name}] completed for {Character.Schema.Name} - progress {Code} ({ProgressAmount}/{Amount})"
        );

        return new None();
    }

    protected async Task<OneOf<AppError, None>> InnerJobAsync(ItemSchema matchingItem)
    {
        logger.LogInformation(
            $"{GetType().Name}: [{Character.Schema.Name}] status for {Character.Schema.Name} - gathering {Code} ({ProgressAmount}/{Amount})"
        );

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
                characterSkillLevel = Character.Schema.AlchemyLevel;
                break;
            case "fishing":
                characterSkillLevel = Character.Schema.CookingLevel;
                break;
            case "mining":
                characterSkillLevel = Character.Schema.MiningLevel;
                break;
            case "woodcutting":
                characterSkillLevel = Character.Schema.WoodcuttingLevel;
                break;
        }

        if (matchingItem.Level > characterSkillLevel)
        {
            return new AppError(
                $"Could not gather item {Code} - current skill level is {characterSkillLevel}, required is {matchingItem.Level}",
                ErrorStatus.InsufficientSkill
            );
        }

        await Character.NavigateTo(Code, ContentType.Resource);

        var result = await Character.Gather();

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
