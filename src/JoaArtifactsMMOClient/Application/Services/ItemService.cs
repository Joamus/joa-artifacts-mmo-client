using System.Security.Principal;
using System.Threading.Tasks;
using Application.ArtifactsApi.Schemas;
using Application.ArtifactsApi.Schemas.Responses;
using Application.Character;
using Application.Errors;
using Application.Jobs;
using Application.Records;
using Applicaton.Services.FightSimulator;

namespace Application.Services;

public static class ItemService
{
    public static readonly List<string> EquipmentItemTypes =
    [
        "weapon",
        "shield",
        "helmet",
        "body_armor",
        "leg_armor",
        "boots",
        "ring",
        "amulet",
        "artifact",
        "rune",
        "bag",
        "utility",
    ];

    public const string TasksCoin = "tasks_coin";

    /**
      * The complexity of creating a wooden_staff, with edge cases of accidentally putting it on etc.,
      * means that we will just skip simming/crafting it.
    **/
    public static string[] ItemSimBlacklist = ["wooden_staff"];

    public static List<ItemSchema> CraftsInto(List<ItemSchema> items, ItemSchema ingredientItem)
    {
        List<ItemSchema> crafts = [];

        foreach (var item in items)
        {
            if (item.Craft is null)
            {
                continue;
            }

            DropSchema? ingredient = item.Craft.Items.FirstOrDefault(_item =>
                _item.Code == ingredientItem.Code
            );

            if (ingredient is not null)
            {
                crafts.Add(item);
            }
        }

        return crafts;
    }

    public static bool IsEquipment(string itemType)
    {
        return new[]
        {
            "consumable",
            "weapon",
            "helmet",
            "body_armor",
            "leg_armor",
            "ring",
            "amulet",
            "artifact",
            "rune",
            "bag",
            "utility",
        }.Contains(itemType);
    }

    public static bool CanUseItem(ItemSchema item, CharacterSchema playerSchema)
    {
        if (item.Conditions is null)
        {
            return true;
        }

        foreach (var condition in item.Conditions)
        {
            int playerLevelOfSkill = 0;

            switch (condition.Code)
            {
                case "level":
                    playerLevelOfSkill = playerSchema.Level;
                    break;
                case "mining_level":
                    playerLevelOfSkill = playerSchema.MiningLevel;
                    break;
                case "alchemy_level":
                    playerLevelOfSkill = playerSchema.AlchemyLevel;
                    break;
                case "fishing_level":
                    playerLevelOfSkill = playerSchema.FishingLevel;
                    break;
                case "woodcutting_level":
                    playerLevelOfSkill = playerSchema.WoodcuttingLevel;
                    break;
            }

            if (
                condition.Operator == ItemConditionOperator.Gt
                && playerLevelOfSkill <= condition.Value
            )
            {
                return false;
            }
        }

        return true;
    }

    public static string[] GetItemSlotsFromItemType(string itemType)
    {
        string[] itemSlots = [];

        switch (itemType)
        {
            case "weapon":
                itemSlots = ["WeaponSlot"];
                break;
            case "rune":
                itemSlots = ["RuneSlot"];
                break;
            case "shield":
                itemSlots = ["ShieldSlot"];
                break;
            case "helmet":
                itemSlots = ["HelmetSlot"];
                break;
            case "body_armor":
                itemSlots = ["BodyArmorSlot"];
                break;
            case "leg_armor":
                itemSlots = ["LegArmorSlot"];
                break;
            case "boots":
                itemSlots = ["BootsSlot"];
                break;
            case "ring":
                itemSlots = ["Ring1Slot", "Ring2Slot"];
                break;
            case "amulet":
                itemSlots = ["AmuletSlot"];
                break;
            case "artifact":
                itemSlots = ["Artifact1Slot", "Artifact2Slot", "Artifact3Slot"];
                break;
            case "utility":
                itemSlots = ["Utility1Slot"];
                break;
            case "bag":
                itemSlots = ["BagSlot"];
                break;
        }

        return itemSlots;
    }

    public static bool IsHealthPotion(ItemSchema item)
    {
        return item.Subtype == "potion"
            && item.Effects.Find(effect => effect.Code == "restore") is not null;
    }

    public static int GetEffect(ItemSchema item, string key)
    {
        return item.Effects.Find(effect => effect.Code == key)?.Value ?? 0;
    }

    public static List<DropSchema> GetFoodToCookFromInventoryList(
        PlayerCharacter character,
        GameState gameState,
        List<DropSchema> itemSource
    )
    {
        Dictionary<string, int> ingredientAmounts = new();
        Dictionary<string, ItemSchema> potentialFoodsToCookDict = new();

        foreach (var item in itemSource)
        {
            var foods = gameState.CraftingLookupDict.GetValueOrNull(item.Code);

            if (foods is not null)
            {
                ingredientAmounts.Add(item.Code, item.Quantity);

                foreach (var food in foods)
                {
                    if (
                        character.Schema.CookingLevel >= food.Level
                        && food.Subtype == "food"
                        && CanUseItem(food, character.Schema)
                    )
                    {
                        if (!potentialFoodsToCookDict.ContainsKey(food.Code))
                        {
                            potentialFoodsToCookDict.Add(food.Code, food);
                        }
                    }
                }
            }
        }

        List<ItemSchema> potentialFoodsToCook = [];

        foreach (var food in potentialFoodsToCookDict)
        {
            potentialFoodsToCook.Add(food.Value);
        }

        CalculationService.SortItemsBasedOnEffect(potentialFoodsToCook, "heal");

        List<DropSchema> foodsToCook = [];

        foreach (var food in potentialFoodsToCook)
        {
            bool hasAllIngredients = true;
            int? amountThatCanBeCooked = null;

            foreach (var ingredient in food.Craft!.Items)
            {
                int amountInInventory = ingredientAmounts.GetValueOrNull(ingredient.Code);

                // If we need more than we have, then we can just skip on to the next food
                if (amountInInventory < ingredient.Quantity)
                {
                    hasAllIngredients = false;
                    break;
                }

                // Integer division
                int amountThatCanBeCookedAccordingToThisIngredient =
                    amountInInventory / ingredient.Quantity;

                // The ingredient that we have the least of, is gonna be the lowest denominator, e.g if we have 10 eggs for a dish requiring 1 egg and 1 fish,
                // then if we have 5 fish then we can only make 5 of the dishes
                if (
                    amountThatCanBeCooked is null
                    || amountThatCanBeCooked > amountThatCanBeCookedAccordingToThisIngredient
                )
                {
                    amountThatCanBeCooked = amountThatCanBeCookedAccordingToThisIngredient;
                }
            }

            List<InventorySlot> ingredientsToSubtract = [];

            if (hasAllIngredients && amountThatCanBeCooked is not null && amountThatCanBeCooked > 0) // always should be
            {
                foreach (var ingredient in food.Craft!.Items)
                {
                    ingredientsToSubtract.Add(
                        new InventorySlot
                        {
                            Code = ingredient.Code,
                            Quantity = amountThatCanBeCooked.Value * ingredient.Quantity,
                        }
                    );
                }

                foreach (var ingredient in ingredientsToSubtract)
                {
                    if (ingredientAmounts.ContainsKey(ingredient.Code))
                    {
                        ingredientAmounts[ingredient.Code] -= ingredient.Quantity;
                    }
                }

                foodsToCook.Add(
                    new DropSchema { Code = food.Code, Quantity = amountThatCanBeCooked.Value }
                );
            }
        }
        return foodsToCook;
    }

    public static ResourceSchema? FindBestResourceToGatherItem(
        PlayerCharacter character,
        GameState gameState,
        string code
    )
    {
        var resources = gameState.Resources.FindAll(resource =>
        {
            bool hasDrop = resource.Drops.Find(drop => drop.Code == code && drop.Rate > 0) != null;

            return hasDrop;
        });

        (ResourceSchema resource, int dropRate)? resourceWithDropRate = null;

        // The higher the drop rate, the lower the number. Drop rate of 1 means 100% chance, rate of 10 is 10% chance, rate of 100 is 1%

        foreach (var resource in resources)
        {
            var bestDrop = resource.Drops.Find(drop =>
            {
                if (resourceWithDropRate is null)
                {
                    return drop.Code == code && drop.Rate > 0;
                }

                var skillLevel = character.GetSkillLevel(SkillService.GetSkillName(resource.Skill));

                bool betterDropRate =
                    drop.Code == code && drop.Rate < resourceWithDropRate.Value.dropRate;

                bool sameDropRateButHigherLevel =
                    drop.Rate == resourceWithDropRate.Value.dropRate
                    && resource.Level > resourceWithDropRate.Value.resource.Level
                    && skillLevel >= resource.Level;

                return betterDropRate || sameDropRateButHigherLevel;
            });

            if (bestDrop is not null)
            {
                resourceWithDropRate = (resource, bestDrop.Rate);
            }
        }

        if (resourceWithDropRate is not null)
        {
            return resourceWithDropRate.Value.resource;
        }

        return null;
    }

    public static bool ArePotionEffectsOverlapping(
        GameState gameState,
        string itemCodeA,
        string itemCodeB
    )
    {
        if (string.IsNullOrEmpty(itemCodeA) || string.IsNullOrEmpty(itemCodeB))
        {
            return false;
        }

        ItemSchema itemA = gameState.ItemsDict.GetValueOrNull(itemCodeA)!;
        ItemSchema itemB = gameState.ItemsDict.GetValueOrNull(itemCodeB)!;

        foreach (var effect in itemA.Effects)
        {
            if (itemB.Effects.Exists(otherEffect => otherEffect.Code == effect.Code))
            {
                return true;
            }
        }

        return false;
    }

    public static async Task<List<EquipmentSlot>> GetBestFightItems(
        PlayerCharacter character,
        GameState gameState,
        MonsterSchema monster,
        List<InventorySlot>? allItemCandidates = null
    )
    {
        if (allItemCandidates is null)
        {
            // 100 quantity for potions, doesn't really matter
            allItemCandidates = gameState
                .Items.Where(item => EquipmentItemTypes.Contains(item.Type))
                .Select(item => new InventorySlot { Code = item.Code, Quantity = 100 })
                .ToList();
        }

        // var relevantMonsters = FightSimulator.GetRelevantMonstersForCharacter(character);

        List<ItemInInventory> itemsForSimming = await GetItemsThatCanBeSimmed(
            character,
            gameState,
            allItemCandidates
                .Select(item => new ItemInInventory
                {
                    Item = gameState.ItemsDict[item.Code],
                    Quantity = item.Quantity,
                })
                .ToList()
            ?? []
        );

        Dictionary<string, EquipmentSlot> relevantItemsDict = [];

        // foreach (var monster in relevantMonsters)
        // {
        var result = FightSimulator.FindBestFightEquipmentWithUsablePotions(
            character,
            gameState,
            monster,
            itemsForSimming
        );

        foreach (var item in result.ItemsToEquip)
        {
            if (!relevantItemsDict.ContainsKey(item.Code))
            {
                relevantItemsDict.Add(item.Code, item);
            }
            else
            {
                relevantItemsDict[item.Code].Quantity += item.Quantity;
            }
        }

        // We don't care if we win or not, we just want to get the best outcome
        // }

        return relevantItemsDict
            .Where(item => gameState.ItemsDict[item.Value.Code].Type != "utility")
            .Select(item => item.Value)
            .ToList();
    }

    public static async Task<List<ItemSchema>> GetBestTools(
        PlayerCharacter character,
        GameState gameState,
        List<InventorySlot>? allItemCandidates = null,
        bool allowUsingTaskMaterials = true
    )
    {
        if (allItemCandidates is null)
        {
            allItemCandidates = gameState
                .Items.Where(item => item.Subtype == "tool")
                .Select(item => new InventorySlot { Code = item.Code, Quantity = 1 })
                .ToList();
        }

        List<ItemSchema> relevantTools = [];

        Dictionary<string, ItemSchema> relevantToolsDict = [];

        List<string> skillNames = SkillService
            .GatheringSkills.Select(SkillService.GetSkillName)
            .Where(skill => skill is not null)
            .ToList();

        var bankData = await gameState.BankItemCache.GetBankItems(character);

        foreach (var item in allItemCandidates)
        {
            var matchingItem = gameState.ItemsDict.GetValueOrNull(item.Code)!;

            if (matchingItem.Subtype != "tool")
            {
                continue;
            }

            if (!allowUsingTaskMaterials)
            {
                if (
                    matchingItem.Craft is not null
                    && matchingItem.Craft.Items.Exists(material =>
                    {
                        var matchingMaterial = gameState.ItemsDict.GetValueOrNull(material.Code);

                        if (matchingMaterial?.Subtype == "task")
                        {
                            return true;
                        }

                        return false;
                    })
                )
                {
                    continue;
                }
            }

            if (!CanUseItem(matchingItem, character.Schema))
            {
                continue;
            }

            var gatheringEffect = matchingItem.Effects.FirstOrDefault(effect =>
                skillNames.Contains(effect.Code)
            );

            if (gatheringEffect is null)
            {
                continue;
            }

            var currentBestTool = relevantToolsDict.GetValueOrNull(gatheringEffect.Code);
            bool isInBank = bankData.Data.Exists(bankItem =>
                bankItem.Code == item.Code && item.Quantity > 0
            );

            if (
                (character.GetItemFromInventory(item.Code)?.Quantity ?? 0) == 0
                && !isInBank
                && !await character.PlayerActionService.CanObtainItem(matchingItem)
            )
            {
                continue;
            }

            if (
                !isInBank
                && matchingItem.Level > character.Schema.Level
                && matchingItem.Craft is not null
            )
            {
                // Essentially we don't watch to spend time crafting super high level items yet,
                // since it usually requires high level materials, and/or high level mob drops.
                // it's good enough to just use worse tools until then,
                // unless maybe a char has enough money to buy really good tools
                continue;
            }

            // The gathering effects have an effect express in negative numbers, e.g. 10% lower cooldown when mining will be -10,
            // so we want to find effects with a lower effect, e.g. -20 is better than -10
            if (
                currentBestTool is null
                || currentBestTool.Effects.Exists(effect => effect.Value > gatheringEffect.Value)
            )
            {
                relevantToolsDict.Remove(gatheringEffect.Code);
                relevantToolsDict.Add(gatheringEffect.Code, matchingItem);
            }
        }

        return relevantToolsDict.Select(candidate => candidate.Value).ToList();
    }

    public static CharacterJob GetObtainOrCraftForJob(
        PlayerCharacter character,
        GameState gameState,
        ItemSchema item,
        int amount
    )
    {
        CharacterJob job;
        if (item.Craft is null || character.Roles.Exists(role => role == item.Craft.Skill))
        {
            job = new ObtainItem(character, gameState, item.Code, amount);
        }
        else
        {
            var crafter = gameState.Characters.FirstOrDefault(character =>
                character.Roles.Exists(role => role == item.Craft.Skill)
            );

            if (crafter is null)
            {
                throw new Exception($"No crafter that has role {item.Craft.Skill}");
            }

            var gatherMaterialsJob = new GatherMaterialsForItem(
                character,
                gameState,
                item.Code,
                amount
            );

            gatherMaterialsJob.Crafter = crafter;

            job = gatherMaterialsJob;
        }
        return job;
    }

    public static ItemSchema? GetBestItemIfUpgrade(ItemSchema a, ItemSchema b)
    {
        var lowestLevelItem = a.Level > b.Level ? b : a;

        var highestLevelItem = lowestLevelItem.Code == a.Code ? b : a;

        foreach (var highLevelEffect in highestLevelItem.Effects)
        {
            // Some effects have "minus" effects, e.g. cooldown reduction for gathering tools,
            // but Obsidian Battleaxe also has minus inventory space, so we don't care for that here.

            bool hasMinusEffect = a.Subtype == "tool" && b.Subtype == "tool";

            var hasSameEffectButBetterOrEqual = lowestLevelItem.Effects.Exists(lowLevelEffect =>
                lowLevelEffect.Code == highLevelEffect.Code
                && (
                    hasMinusEffect
                        ? highLevelEffect.Value < lowLevelEffect.Value
                        : highLevelEffect.Value >= lowLevelEffect.Value
                )
            );

            if (!hasSameEffectButBetterOrEqual)
            {
                return null;
            }
        }

        return highestLevelItem;
    }

    public static async Task<List<ItemInInventory>> GetItemsThatCanBeSimmed(
        PlayerCharacter character,
        GameState gameState,
        List<ItemInInventory> allItemCandidates
    )
    {
        List<ItemInInventory> items = [];

        var bankItems = await gameState.BankItemCache.GetBankItems(character, true);

        var bankItemDict = new Dictionary<string, DropSchema>();

        foreach (var item in bankItems.Data)
        {
            // Cloning for changing the quantity
            bankItemDict.Add(item.Code, item with { });
        }

        foreach (var item in allItemCandidates)
        {
            var matchingItem = item.Item;

            if (ItemSimBlacklist.Contains(matchingItem.Code))
            {
                continue;
            }

            if (matchingItem.Subtype == "tool")
            {
                continue;
            }

            if (!EquipmentItemTypes.Contains(matchingItem.Type))
            {
                continue;
            }

            if (!CanUseItem(matchingItem, character.Schema))
            {
                continue;
            }

            int amountInBank = bankItemDict.GetValueOrNull(matchingItem.Code)?.Quantity ?? 0;

            var itemOnCharacter = character.GetEquippedItemOrInInventory(matchingItem.Code);

            int amountEquippedOrInInventory = itemOnCharacter is null
                ? 0
                : itemOnCharacter.Sum(item => item.inventorySlot.Quantity);

            int amountAvailable = amountInBank + amountEquippedOrInInventory;

            var matchingNpcItem = gameState.NpcItemsDict.GetValueOrDefault(matchingItem.Code);

            if (amountAvailable == 0)
            {
                if (matchingNpcItem is not null)
                {
                    // For now, don't try to grind gold or anything for these items.
                    if (
                        matchingNpcItem.Currency == "gold"
                        && character.Schema.Gold < matchingNpcItem.BuyPrice
                    )
                    {
                        continue;
                    }

                    if (gameState.EventService.WhereIsEntityActive(matchingNpcItem.Npc) is null)
                    {
                        continue;
                    }
                }
                else if (matchingItem.Craft is null)
                {
                    continue;
                }

                if (!await character.PlayerActionService.CanObtainItem(matchingItem))
                {
                    continue;
                }
                else if (gameState.EventService.IsItemFromEventMonster(matchingItem.Code, true))
                {
                    continue;
                }
            }

            items.Add(new ItemInInventory { Item = matchingItem, Quantity = item.Quantity });
        }

        return items;
    }

    public static string GetBestUtilityEffect(
        PlayerCharacter character,
        GameState gameState,
        string entityCode
    )
    {
        int entityLevel;
        int characterLevel;

        var matchingMonster = gameState.MonstersDict.GetValueOrNull(entityCode);

        if (matchingMonster is not null)
        {
            entityLevel = matchingMonster.Level;
            characterLevel = character.Schema.Level;
        }
        else
        {
            var matchingResource = FindBestResourceToGatherItem(character, gameState, entityCode);

            if (matchingResource is null)
            {
                throw new AppError($"Could not find resource for code \"{entityCode}\"");
            }

            entityLevel = matchingResource.Level;
            characterLevel = character.GetSkillLevel(
                SkillService.GetSkillName(matchingResource.Skill)
            );
        }

        if (characterLevel > entityLevel + PlayerActionService.LEVEL_DIFF_NO_XP)
        {
            return Effect.Prospecting;
        }

        return Effect.Wisdom;
    }
}
