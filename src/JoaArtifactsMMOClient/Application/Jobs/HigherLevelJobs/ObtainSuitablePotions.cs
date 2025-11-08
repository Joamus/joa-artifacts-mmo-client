using Application.Artifacts.Schemas;
using Application.ArtifactsApi.Schemas;
using Application.ArtifactsApi.Schemas.Requests;
using Application.Character;
using Application.Errors;
using Application.Records;
using Application.Services;
using Applicaton.Services.FightSimulator;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public class ObtainSuitablePotions : CharacterJob
{
    private readonly int _amount;

    public static readonly int POTION_BATCH_SIZE = 10;

    public MonsterSchema Monster;

    public ObtainSuitablePotions(
        PlayerCharacter playerCharacter,
        GameState gameState,
        int amount,
        MonsterSchema monster
    )
        : base(playerCharacter, gameState)
    {
        _amount = amount;
        Monster = monster;
    }

    protected override async Task<OneOf<AppError, None>> ExecuteAsync()
    {
        logger.LogInformation(
            $"{JobName}: [{Character.Schema.Name}] run started - need to find {_amount} potions"
        );

        // If still not enough, we just go gather and cook some - be biased towards fishing, fastest way to get food

        var jobs = await GetAcquirePotionJobs(
            Character,
            gameState,
            GetPotionsToObtain(Character),
            Monster
        );

        logger.LogInformation(
            $"{JobName}: [{Character.Schema.Name}] found {jobs.Count} jobs - need to find {_amount} potions"
        );
        if (jobs.Count > 0)
        {
            Character.QueueJobsAfter(Id, jobs);
        }

        return new None();
    }

    public static int GetPotionsToObtain(PlayerCharacter character)
    {
        // We want to ensure that we don't fill our inventory
        int inventorySpaceLeft = character.GetInventorySpaceLeft();

        return Math.Min(PlayerActionService.MAX_AMOUNT_UTILITY_SLOT, inventorySpaceLeft / 2);
    }

    public static async Task<List<CharacterJob>> GetAcquirePotionJobs(
        PlayerCharacter character,
        GameState gameState,
        int preferedAmount,
        MonsterSchema monster
    )
    {
        List<(ItemSchema item, bool canCraft, int amountInBank)> potionCandidates = [];

        var bankItemsResponse = await gameState.BankItemCache.GetBankItems(character);

        foreach (var element in gameState.UtilityItemsDict)
        {
            var item = element.Value;
            int restoreEffect = ItemService.GetEffect(item, "restore");

            if (restoreEffect == 0)
            {
                continue;
            }

            if (!ItemService.CanUseItem(item, character.Schema))
            {
                continue;
            }

            var canCraftItem =
                item.Craft?.Skill == Skill.Alchemy
                && character.Schema.AlchemyLevel >= item.Craft.Level;

            int amountInBank =
                bankItemsResponse
                    .Data.FirstOrDefault(bankItem => bankItem.Code == item.Code)
                    ?.Quantity ?? 0;

            if (!canCraftItem && amountInBank == 0)
            {
                continue;
            }

            potionCandidates.Add((item, canCraftItem, amountInBank));
        }

        potionCandidates.Sort(
            (b, a) =>
                ItemService
                    .GetEffect(a.item, "restore")
                    .CompareTo(ItemService.GetEffect(b.item, "restore"))
        );

        List<ItemInInventory> potionsForSim = [];

        foreach (var candiate in potionCandidates)
        {
            potionsForSim.Add(
                new ItemInInventory
                {
                    Item = candiate.item,

                    Quantity = PlayerActionService.MAX_AMOUNT_UTILITY_SLOT,
                }
            );
        }

        var fightSim = FightSimulator.FindBestFightEquipment(
            character,
            gameState,
            monster,
            potionsForSim
        );

        potionCandidates = potionCandidates
            .Where(candidate =>
                fightSim.ItemsToEquip.Exists(item => item.Code == candidate.item.Code)
            )
            .ToList();

        // There should only be two
        if (potionCandidates.Count > 2)
        {
            potionCandidates = potionCandidates.GetRange(0, 2);
        }

        List<CharacterJob> resultJobs = [];

        int amountLeft = preferedAmount;
        // Implement finding the 2 best pots, if any, and equip. Use up stuff from the bank.

        foreach (var potion in potionCandidates)
        {
            if (potion.amountInBank > 0)
            {
                var amount = Math.Min(
                    character.GetInventorySpaceLeft() - 1,
                    Math.Min(preferedAmount, potion.amountInBank)
                );

                if (amount > 0)
                {
                    var job = new WithdrawItem(character, gameState, potion.item.Code, amount);

                    amountLeft = amountLeft - amount;
                    resultJobs.Add(job);

                    // Craft it or learn to craft it, if needed.
                    job.CanTriggerObtain = true;
                }
                else
                {
                    break;
                }
            }
        }

        if (amountLeft > 0)
        {
            foreach (var potion in potionCandidates)
            {
                int ingredientsRequiredToCraftOne = 0;

                foreach (var material in potion.item.Craft!.Items)
                {
                    ingredientsRequiredToCraftOne += material.Quantity;
                }

                int totalIngredientsRequired = ingredientsRequiredToCraftOne * amountLeft;

                int iterations = (int)
                    Math.Ceiling((double)totalIngredientsRequired / POTION_BATCH_SIZE);

                for (int i = 0; i < iterations; i++)
                {
                    int amountToCraft = Math.Min(amountLeft, POTION_BATCH_SIZE);

                    if (amountLeft <= 0)
                    {
                        break;
                    }
                    string itemCode = potion.item.Code;

                    var job = new ObtainItem(character, gameState, itemCode, amountToCraft);
                    job.AllowUsingMaterialsFromBank = true;

                    amountLeft = amountLeft - amountToCraft;
                    resultJobs.Add(job);
                }
            }
            amountLeft = 0;
        }

        foreach (var job in resultJobs)
        {
            job.onSuccessEndHook = async () =>
            {
                int amountInInventory = character.GetItemFromInventory(job.Code)?.Quantity ?? 0;

                character.Logger.LogDebug(
                    $"GetAcquirePotionJobs: [{character.Schema.Name}] onSuccessEndHook: Trying to equip {amountInInventory} x {job.Code}"
                );
                if (amountInInventory > 0)
                {
                    bool availableInUtil1 =
                        character.Schema.Utility1SlotQuantity
                            < PlayerActionService.MAX_AMOUNT_UTILITY_SLOT
                        && (
                            character.Schema.Utility1Slot == ""
                            || character.Schema.Utility1Slot == job.Code
                        );

                    bool availableInUtil2 =
                        character.Schema.Utility2SlotQuantity
                            < PlayerActionService.MAX_AMOUNT_UTILITY_SLOT
                        && (
                            character.Schema.Utility2Slot == ""
                            || character.Schema.Utility2Slot == job.Code
                        );

                    if (availableInUtil1 || availableInUtil2)
                    {
                        character.Logger.LogDebug(
                            $"GetAcquirePotionJobs: [{character.Schema.Name}] onSuccessEndHook: Smart equipping {amountInInventory} x {job.Code}"
                        );
                        await character.SmartItemEquip(job.Code, amountInInventory);
                    }
                    else
                    {
                        character.Logger.LogDebug(
                            $"GetAcquirePotionJobs: [{character.Schema.Name}] onSuccessEndHook: No available util slots for equipping {amountInInventory} x {job.Code} - depositting"
                        );

                        await character.NavigateTo("bank", ContentType.Bank);

                        await character.DepositBankItem(
                            [
                                new WithdrawOrDepositItemRequest
                                {
                                    Code = job.Code,
                                    Quantity = amountInInventory,
                                },
                            ]
                        );
                    }
                }
                else
                {
                    character.Logger.LogDebug(
                        $"GetAcquirePotionJobs: [{character.Schema.Name}] onSuccessEndHook: Found {amountInInventory} x {job.Code} in inventory - not equipping"
                    );
                }
            };
        }

        return resultJobs;
    }
}
