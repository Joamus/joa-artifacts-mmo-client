using Application.Artifacts.Schemas;
using Application.ArtifactsApi.Schemas;
using Application.ArtifactsApi.Schemas.Responses;
using Application.Character;
using Application.Errors;
using Application.Services;
using Applicaton.Jobs;
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
            if (DepositUnneededItems.ShouldInitDepositItems(Character, false))
            {
                await Character.QueueJobsBefore(
                    Id,
                    [new DepositUnneededItems(Character, gameState)]
                );
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

            var resource = ItemService.FindBestResourceToGatherItem(Character, gameState, Code);

            if (resource is null)
            {
                return new AppError($"Could not find a resource to gather {Code} from");
            }

            Skill skill = resource.Skill;

            var result = await InnerJobAsync(matchingItem, resource, skill);

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

    protected async Task<OneOf<AppError, None>> InnerJobAsync(
        ItemSchema matchingItem,
        ResourceSchema resource,
        Skill skill
    )
    {
        logger.LogInformation(
            $"{JobName}: [{Character.Schema.Name}] status for {Character.Schema.Name} - gathering {Code} ({ProgressAmount}/{Amount})"
        );

        // if (matchingItem.Type != "resource" || !_allowedSubtypes.Contains(matchingItem.Subtype))
        // {
        //     return new AppError(
        //         $"Item with code: {Code} - type: {matchingItem.Type} - sub type: {matchingItem.Type} is not a gatherable resource"
        //     );
        // }

        int characterSkillLevel = 0;

        switch (resource.Skill)
        {
            case Skill.Alchemy:
                characterSkillLevel = Character.Schema.AlchemyLevel;
                break;
            case Skill.Fishing:
                characterSkillLevel = Character.Schema.FishingLevel;
                break;
            case Skill.Mining:
                characterSkillLevel = Character.Schema.MiningLevel;
                break;
            case Skill.Woodcutting:
                characterSkillLevel = Character.Schema.WoodcuttingLevel;
                break;
        }

        if (resource.Level > characterSkillLevel)
        {
            if (CanTriggerTraining)
            {
                logger.LogInformation(
                    $"{JobName}: [{Character.Schema.Name}] has too low gathering skill ({characterSkillLevel}/{matchingItem.Level}) in {matchingItem.Subtype} - training until they can craft the item"
                );
                // Skill skill = (Skill)SkillService.GetSkillFromName(matchingItem.Subtype)!;

                await Character.QueueJobsBefore(
                    Id,
                    [new TrainSkill(Character, gameState, resource.Skill, matchingItem.Level)]
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

        // var resource = ItemService.FindBestResourceToGatherItem(Character, gameState, Code);

        // if (resource is null)
        // {
        //     return new AppError(
        //         $"{JobName}: [{Character.Schema.Name}] appError: Could not find resource to gather {Code}",
        //         ErrorStatus.InsufficientSkill
        //     );
        // }
        await Character.NavigateTo(resource.Code);

        await Character.PlayerActionService.EquipBestGatheringEquipment(skill);
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

    public static bool CanGatherResource(ResourceSchema resource, CharacterSchema characterSchema)
    {
        int characterSkillLevel = 0;

        switch (resource.Skill)
        {
            case Skill.Alchemy:
                characterSkillLevel = characterSchema.AlchemyLevel;
                break;
            case Skill.Fishing:
                characterSkillLevel = characterSchema.FishingLevel;
                break;
            case Skill.Mining:
                characterSkillLevel = characterSchema.MiningLevel;
                break;
            case Skill.Woodcutting:
                characterSkillLevel = characterSchema.WoodcuttingLevel;
                break;
        }

        return characterSkillLevel >= resource.Level;
    }
}
