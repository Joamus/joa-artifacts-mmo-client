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
        onSuccessEndHook = async () =>
        {
            logger.LogInformation(
                $"{JobName}: [{Character.Schema.Name}] onSuccessEndHook: queueing job to deposit {Amount} x {Code} to the bank"
            );
            var depositItemJob = new DepositItems(Character, gameState, Code, Amount);

            await Character.QueueJob(depositItemJob, true);
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
        var resource = ItemService
            .FindBestResourceToGatherItem(Character, gameState, Code)
            ?.Resource;

        if (resource is null)
        {
            return new AppError($"Could not find a resource to gather {Code} from");
        }

        Skill skill = resource.Skill;

        List<CharacterJob> itemJobs = await GetItemJobsIfBetterToolInBank(
            Character,
            gameState,
            skill
        );

        if (itemJobs.Count > 0)
        {
            logger.LogInformation(
                $"{JobName}: [{Character.Schema.Name}] found job to withdraw better tool to gather {resource.Name}"
            );

            await Character.QueueJobsBefore(Id, itemJobs);
            Status = JobStatus.Suspend;
            return new None();
        }

        while (ProgressAmount < Amount)
        {
            if (DepositUnneededItems.ShouldInitDepositItems(Character, false))
            {
                await Character.QueueJobsBefore(
                    Id,
                    [new DepositUnneededItems(Character, gameState, null, false)]
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

        if (!CanGatherResource(resource, Character.Schema))
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

        var jobsNeededForNavigationResult =
            await Character.PlayerActionService.NavigationService.GetJobsNeededForNavigation(
                resource.Code
            );

        if (jobsNeededForNavigationResult.Value is AppError)
        {
            return jobsNeededForNavigationResult.AsT0;
        }

        var jobsNeededForNavigation = jobsNeededForNavigationResult.AsT1;

        if (jobsNeededForNavigation.Count > 0)
        {
            logger.LogInformation(
                "{JobName}: [{Character.Schema.Name}] need to do {count} jobs before we can navigate to {Code}",
                JobName,
                Character.Schema.Name,
                jobsNeededForNavigation.Count,
                Code
            );
            await Character.QueueJobsBefore(Id, jobsNeededForNavigation);
            Status = JobStatus.Suspend;
            return new None();
        }

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

    public static async Task<List<CharacterJob>> GetItemJobsIfBetterToolInBank(
        PlayerCharacter character,
        GameState gameState,
        Skill skill
    )
    {
        List<ItemSchema> availableToolsOnCharacter = [];

        var equippedItem = !string.IsNullOrWhiteSpace(character.Schema.WeaponSlot)
            ? gameState.ItemsDict.GetValueOrNull(character.Schema.WeaponSlot)
            : null;

        if (equippedItem is not null && ItemService.IsToolForSkill(equippedItem, skill))
        {
            availableToolsOnCharacter.Add(equippedItem);
        }

        var bankResponse = await gameState.BankItemCache.GetBankItems(character);

        var toolsFromBank = bankResponse
            .Where(item =>
            {
                if (string.IsNullOrWhiteSpace(item.Code))
                {
                    return false;
                }

                var matchingItem = gameState.ItemsDict[item.Code];

                return ItemService.IsToolForSkill(matchingItem, skill)
                    && ItemService.CanUseItem(matchingItem, character.Schema);
            })
            .Select(item => gameState.ItemsDict[item.Code])
            .ToList();

        foreach (var item in character.Schema.Inventory)
        {
            if (string.IsNullOrWhiteSpace(item.Code))
            {
                continue;
            }

            var matchingItem = gameState.ItemsDict[item.Code];

            if (
                ItemService.IsToolForSkill(matchingItem, skill)
                && ItemService.CanUseItem(matchingItem, character.Schema)
            )
            {
                availableToolsOnCharacter.Add(matchingItem);
            }
        }

        string skillName = SkillService.GetSkillName(skill);

        // Remember, a -10 gather effect is worse than a -20

        CalculationService.SortItemsBasedOnEffect(availableToolsOnCharacter, skillName, true);
        CalculationService.SortItemsBasedOnEffect(toolsFromBank, skillName, true);

        var bestItemOnCharacter = availableToolsOnCharacter.FirstOrDefault();
        var bestItemInBank = toolsFromBank.FirstOrDefault();

        // There is nothing at all to get from the bank, so we cannot withdraw
        if (bestItemInBank is null)
        {
            return [];
        }

        if (bestItemOnCharacter is null)
        {
            return [new WithdrawItem(character, gameState, bestItemInBank.Code, 1)];
        }

        // We already have the same tool
        if (bestItemOnCharacter.Code == bestItemInBank.Code)
        {
            return [];
        }

        // We already have the same tool
        if (bestItemOnCharacter.Code != bestItemInBank.Code)
        {
            int bestItemOnCharacterEffect = bestItemOnCharacter
                .Effects.First(effect => effect.Code == skillName)
                .Value;

            int bestItemInBankEffect = bestItemInBank
                .Effects.First(effect => effect.Code == skillName)
                .Value;

            if (bestItemInBankEffect < bestItemOnCharacterEffect)
            {
                return
                [
                    new WithdrawItem(character, gameState, bestItemInBank.Code, 1),
                    new DepositItems(character, gameState, bestItemOnCharacter.Code, 1),
                ];
            }
        }

        if (bestItemOnCharacter is null && bestItemInBank is null)
        {
            return [];
        }

        return [];
    }
}
