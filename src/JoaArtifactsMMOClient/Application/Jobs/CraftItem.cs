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

    public CraftItem(PlayerCharacter playerCharacter, string code, int amount)
        : base(playerCharacter)
    {
        _code = code;
        _amount = amount;
    }

    public override async Task<OneOf<JobError, None>> RunAsync()
    {
        if (DepositUnneededItems.ShouldInitDepositItems(_playerCharacter))
        {
            _playerCharacter.QueueJobsBefore(Id, [new DepositUnneededItems(_playerCharacter)]);
            return new None();
        }

        var matchingItem = _gameState.Items.Find(item => item.Code == _code);

        if (matchingItem is null || matchingItem.Craft is null)
        {
            return new JobError(
                $"Could not find craftable item with code {_code} - could not craft it"
            );
        }

        int characterSkillLevel = 0;
        string craftingLocationCode = "";

        switch (matchingItem.Craft.Skill)
        {
            case Artifacts.Schemas.Skill.Alchemy:
                characterSkillLevel = _playerCharacter._character.AlchemyLevel;
                craftingLocationCode = "alchemy";
                break;
            case Artifacts.Schemas.Skill.Cooking:
                characterSkillLevel = _playerCharacter._character.CookingLevel;
                craftingLocationCode = "cooking";
                break;
            case Artifacts.Schemas.Skill.Gearcrafting:
                characterSkillLevel = _playerCharacter._character.GearcraftingLevel;
                craftingLocationCode = "gearcrafting";
                break;
            case Artifacts.Schemas.Skill.Jewelrycrafting:
                characterSkillLevel = _playerCharacter._character.JewelrycraftingLevel;
                craftingLocationCode = "jewelrycrafting";
                break;
            case Artifacts.Schemas.Skill.Mining:
                characterSkillLevel = _playerCharacter._character.MiningLevel;
                craftingLocationCode = "mining";
                break;
            case Artifacts.Schemas.Skill.Weaponcrafting:
                characterSkillLevel = _playerCharacter._character.WeaponcraftingLevel;
                craftingLocationCode = "weaponcrafting";
                break;
            case Artifacts.Schemas.Skill.Woodcutting:
                characterSkillLevel = _playerCharacter._character.WoodcuttingLevel;
                craftingLocationCode = "woodcutting";
                break;
        }

        if (matchingItem.Craft.Level > characterSkillLevel)
        {
            return new JobError(
                $"Could not craft item {_code} - current skill level is {characterSkillLevel}, required is {matchingItem.Craft.Level}",
                JobStatus.InsufficientSkill
            );
        }

        if (craftingLocationCode == "")
        {
            return new JobError(
                $"Could not craft item {_code} - could not find workshop to go to - skill is {matchingItem.Craft.Skill}"
            );
        }

        await _playerCharacter.NavigateTo(craftingLocationCode, ContentType.Workshop);

        await _playerCharacter.Craft(_code, _amount);

        return new None();
    }
}
