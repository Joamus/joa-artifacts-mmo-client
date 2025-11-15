using Application.ArtifactsApi.Schemas;
using Application.Character;
using Application.Errors;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public class CraftItem : CharacterJob
{
    protected int progressAmount { get; set; } = 0;

    public bool CanTriggerTraining { get; set; } = true;
    public bool CanTriggerObtain { get; set; } = true;

    public CraftItem(PlayerCharacter playerCharacter, GameState gameState, string code, int amount)
        : base(playerCharacter, gameState)
    {
        Code = code;
        Amount = amount;
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
            $"{JobName}: [{Character.Schema.Name}] run started - progress {Code} ({progressAmount}/{Amount})"
        );

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

        List<DropSchema> missingMaterials = [];

        foreach (var material in matchingItem.Craft.Items)
        {
            if (
                (Character.GetItemFromInventory(material.Code)?.Quantity ?? 0)
                < material.Quantity * Amount
            )
            {
                missingMaterials.Add(
                    new DropSchema { Code = material.Code, Quantity = material.Quantity * Amount }
                );
            }
        }

        if (missingMaterials.Count > 0)
        {
            if (CanTriggerObtain)
            {
                logger.LogWarning(
                    $"{JobName}: [{Character.Schema.Name}]: {missingMaterials.Count} materials were missing from inventory - triggering an obtain of them"
                );

                List<CharacterJob> jobs = [];
                foreach (var material in missingMaterials)
                {
                    var job = new ObtainOrFindItem(
                        Character,
                        gameState,
                        material.Code,
                        material.Quantity * Amount
                    );
                    jobs.Add(job);
                }

                Character.QueueJobsBefore(Id, jobs);
                Status = JobStatus.Suspend;
                return new None();
            }
            else
            {
                return new AppError(
                    $"{JobName}: [{Character.Schema.Name}] appError: {missingMaterials.Count} materials were missing from inventory, could not craft item"
                );
            }
        }

        await Character.NavigateTo(craftingLocationCode);

        await Character.Craft(Code, Amount);

        return new None();
    }
}
