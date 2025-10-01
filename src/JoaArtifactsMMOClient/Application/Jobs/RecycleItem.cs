using Application.ArtifactsApi.Schemas;
using Application.Character;
using Application.Errors;
using Applicaton.Jobs;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public class RecycleItem : CharacterJob
{
    public int Amount { get; private set; }

    private List<DropSchema> recycledDrops { get; set; } = [];

    public RecycleItem(
        PlayerCharacter playerCharacter,
        GameState gameState,
        string code,
        int amount
    )
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
                $"{GetType().Name}: [{Character.Schema.Name}] onSuccessEndHook: queueing job to deposit {Amount} x {Code} to the bank"
            );

            foreach (var drop in recycledDrops)
            {
                var depositItemJob = new DepositItems(
                    Character,
                    gameState,
                    drop.Code,
                    drop.Quantity
                );
                Character.QueueJob(depositItemJob, true);
            }

            return Task.Run(() => { });
        };
    }

    protected override async Task<OneOf<AppError, None>> ExecuteAsync()
    {
        logger.LogInformation(
            $"{GetType().Name}: [{Character.Schema.Name}] run started - recycle {Amount} x {Code}"
        );

        if (DepositUnneededItems.ShouldInitDepositItems(Character))
        {
            Character.QueueJobsBefore(
                Id,
                [new DepositUnneededItems(Character, gameState).SetParent<RecycleItem>(this)]
            );
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
        string? craftingLocationCode = null;

        switch (matchingItem.Craft.Skill)
        {
            case Artifacts.Schemas.Skill.Gearcrafting:
                craftingLocationCode = "gearcrafting";
                break;
            case Artifacts.Schemas.Skill.Jewelrycrafting:
                craftingLocationCode = "jewelrycrafting";
                break;
            case Artifacts.Schemas.Skill.Weaponcrafting:
                craftingLocationCode = "weaponcrafting";
                break;
        }

        if (craftingLocationCode is null)
        {
            return new AppError($"Could not find location to recycle {Code}");
        }
        await Character.NavigateTo(craftingLocationCode, ContentType.Workshop);

        var result = await Character.Recycle(Code, Amount);

        recycledDrops = result.Data.Details.Items;

        return new None();
    }
}
