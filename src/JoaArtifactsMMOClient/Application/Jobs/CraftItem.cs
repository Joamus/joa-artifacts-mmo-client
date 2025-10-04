using Application.ArtifactsApi.Schemas;
using Application.Character;
using Application.Errors;
using Applicaton.Jobs;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public class CraftItem : CharacterJob
{
    public int Amount { get; private set; }

    protected int progressAmount { get; set; } = 0;

    public bool CanTriggerTraining { get; set; } = true;

    public CraftItem(PlayerCharacter playerCharacter, GameState gameState, string code, int amount)
        : base(playerCharacter, gameState)
    {
        Code = code;
        Amount = amount;
    }

    public void ForBank()
    {
        onSuccessEndHook += () =>
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
            $"{JobName}: [{Character.Schema.Name}] run started - progress {Code} ({progressAmount}/{Amount})"
        );

        if (DepositUnneededItems.ShouldInitDepositItems(Character))
        {
            Character.QueueJobsBefore(Id, [new DepositUnneededItems(Character, gameState)]);
            Status = JobStatus.Suspend;
            return new None();
        }

        var matchingItem = gameState.Items.Find(item => item.Code == Code);

        if (matchingItem is null || matchingItem.Craft is null)
        {
            return new AppError(
                $"Could not find craftable item with code {Code} - could not craft it"
            );
        }

        int characterSkillLevel = 0;
        string craftingLocationCode = "";

        switch (matchingItem.Craft.Skill)
        {
            case Artifacts.Schemas.Skill.Alchemy:
                characterSkillLevel = Character.Schema.AlchemyLevel;
                craftingLocationCode = "alchemy";
                break;
            case Artifacts.Schemas.Skill.Cooking:
                characterSkillLevel = Character.Schema.CookingLevel;
                craftingLocationCode = "cooking";
                break;
            case Artifacts.Schemas.Skill.Gearcrafting:
                characterSkillLevel = Character.Schema.GearcraftingLevel;
                craftingLocationCode = "gearcrafting";
                break;
            case Artifacts.Schemas.Skill.Jewelrycrafting:
                characterSkillLevel = Character.Schema.JewelrycraftingLevel;
                craftingLocationCode = "jewelrycrafting";
                break;
            case Artifacts.Schemas.Skill.Mining:
                characterSkillLevel = Character.Schema.MiningLevel;
                craftingLocationCode = "mining";
                break;
            case Artifacts.Schemas.Skill.Weaponcrafting:
                characterSkillLevel = Character.Schema.WeaponcraftingLevel;
                craftingLocationCode = "weaponcrafting";
                break;
            case Artifacts.Schemas.Skill.Woodcutting:
                characterSkillLevel = Character.Schema.WoodcuttingLevel;
                craftingLocationCode = "woodcutting";
                break;
        }

        if (matchingItem.Craft.Level > characterSkillLevel)
        {
            if (CanTriggerTraining)
            {
                logger.LogInformation(
                    $"{JobName}: [{Character.Schema.Name}] has too low crafting skill ({characterSkillLevel}/{matchingItem.Craft.Level}) in {craftingLocationCode} - training until they can craft the item"
                );
                Character.QueueJobsBefore(
                    Id,
                    [
                        new TrainSkill(
                            Character,
                            gameState,
                            matchingItem.Craft.Skill,
                            matchingItem.Craft.Level
                        ),
                    ]
                );
                Status = JobStatus.Suspend;
                return new None();
            }
            else
            {
                return new AppError(
                    $"Could not craft item {Code} - current skill level is {characterSkillLevel}, required is {matchingItem.Craft.Level}",
                    ErrorStatus.InsufficientSkill
                );
            }
        }

        if (craftingLocationCode == "")
        {
            return new AppError(
                $"Could not craft item {Code} - could not find workshop to go to - skill is {matchingItem.Craft.Skill}"
            );
        }

        await Character.NavigateTo(craftingLocationCode, ContentType.Workshop);

        await Character.Craft(Code, Amount);

        return new None();
    }
}
