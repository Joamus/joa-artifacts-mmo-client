using System.Reflection.Metadata.Ecma335;
using System.Security.Permissions;
using Application.Artifacts.Schemas;
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

    public bool AllowUsingInventory { get; set; } = false;

    public bool CanTriggerTraining { get; set; } = true;

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

    public void ForBank()
    {
        onSuccessEndHook = () =>
        {
            logger.LogInformation(
                $"{JobName}: [{Character.Schema.Name}] onSuccessEndHook: queueing job to deposit {Amount} x {Code} to the bank"
            );
            var depositItemJob = new DepositItems(Character, gameState, Code, Amount);

            Character.QueueJob(depositItemJob, true);

            return Task.Run(() => { });
        };
    }

    protected override async Task<OneOf<AppError, None>> ExecuteAsync()
    {
        logger.LogInformation(
            $"{JobName}: [{Character.Schema.Name}] run started - progress {Code} ({ProgressAmount}/{Amount})"
        );

        // In case of resuming a task
        ShouldInterrupt = false;

        if (AllowUsingInventory)
        {
            int amountInInventory = Character.GetItemFromInventory(Code)?.Quantity ?? 0;

            ProgressAmount = amountInInventory;

            logger.LogInformation(
                $"{JobName}: [{Character.Schema.Name}] found {amountInInventory} in inventory - progress {Code} ({ProgressAmount}/{Amount})"
            );
        }

        while (ProgressAmount < Amount)
        {
            if (DepositUnneededItems.ShouldInitDepositItems(Character))
            {
                Character.QueueJobsBefore(Id, [new DepositUnneededItems(Character, gameState)]);
                Status = JobStatus.Suspend;
                return new None();
            }

            if (ShouldInterrupt)
            {
                Status = JobStatus.Suspend;
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
                    Status = JobStatus.Failed;
                    return jobError;
                default:
                    // Just continue
                    break;
            }

            if (Status == JobStatus.Suspend)
            {
                // Queued other jobs before this job
                return new None();
            }
        }

        logger.LogInformation(
            $"{JobName}: [{Character.Schema.Name}] completed for {Character.Schema.Name} - progress {Code} ({ProgressAmount}/{Amount})"
        );

        return new None();
    }

    protected async Task<OneOf<AppError, None>> InnerJobAsync(ItemSchema matchingItem)
    {
        logger.LogInformation(
            $"{JobName}: [{Character.Schema.Name}] status for {Character.Schema.Name} - gathering {Code} ({ProgressAmount}/{Amount})"
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
                characterSkillLevel = Character.Schema.FishingLevel;
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
            if (CanTriggerTraining)
            {
                logger.LogInformation(
                    $"{JobName}: [{Character.Schema.Name}] has too low gathering skill ({characterSkillLevel}/{matchingItem.Level}) in {matchingItem.Subtype} - training until they can craft the item"
                );
                Skill skill = TrainSkill.GetSkillFromName(matchingItem.Subtype);

                Character.QueueJobsBefore(
                    Id,
                    [new TrainSkill(Character, gameState, skill, matchingItem.Level)]
                );
                Status = JobStatus.Suspend;
                return new None();
            }
            else
            {
                logger.LogInformation(
                    $"{JobName}: [{Character.Schema.Name}] has too low gathering skill ({characterSkillLevel}/{matchingItem.Level}) in {matchingItem.Subtype}"
                );
                return new AppError(
                    $"Could not gather item {Code} - current skill level is {characterSkillLevel}, required is {matchingItem.Level}",
                    ErrorStatus.InsufficientSkill
                );
            }
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
