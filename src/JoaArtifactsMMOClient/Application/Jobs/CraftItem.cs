using System.Diagnostics;
using Application;
using Application.ArtifactsApi.Schemas;
using Application.Character;
using Application.Errors;
using Application.Jobs;
using Application.Services;
using Applicaton.Jobs;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public class CraftItem : CharacterJob
{
    protected string _code { get; init; }
    protected int _amount { get; set; }

    protected int _progressAmount { get; set; } = 0;

    public CraftItem(PlayerCharacter playerCharacter, GameState gameState, string code, int amount)
        : base(playerCharacter, gameState)
    {
        _code = code;
        _amount = amount;
    }

    protected override async Task<OneOf<AppError, None>> ExecuteAsync()
    {
        if (DepositUnneededItems.ShouldInitDepositItems(Character))
        {
            Character.QueueJobsBefore(Id, [new DepositUnneededItems(Character, gameState)]);
            return new None();
        }

        var matchingItem = gameState.Items.Find(item => item.Code == _code);

        if (matchingItem is null || matchingItem.Craft is null)
        {
            return new AppError(
                $"Could not find craftable item with code {_code} - could not craft it"
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
            return new AppError(
                $"Could not craft item {_code} - current skill level is {characterSkillLevel}, required is {matchingItem.Craft.Level}",
                ErrorStatus.InsufficientSkill
            );
        }

        if (craftingLocationCode == "")
        {
            return new AppError(
                $"Could not craft item {_code} - could not find workshop to go to - skill is {matchingItem.Craft.Skill}"
            );
        }

        await Character.NavigateTo(craftingLocationCode, ContentType.Workshop);

        await Character.Craft(_code, _amount);

        return new None();
    }
}
