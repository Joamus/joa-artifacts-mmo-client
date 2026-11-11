using System.Collections.Frozen;
using Application.Artifacts.Schemas;
using Application.ArtifactsApi.Schemas;
using Application.Character;
using Application.Jobs;
using Microsoft.OpenApi.Extensions;

namespace Application.Services;

public class SeasonalOfferingService
{
    GameState gameState { get; init; }
    ILogger Logger { get; init; }
    const int MAX_OFFERINGS = 3;

    const string AFavoredChampion = "a_favored_champion";

    public SeasonalOfferingService(ILogger logger, GameState gameState)
    {
        Logger = logger;
        this.gameState = gameState;
    }

    public async Task<List<CharacterJob>> GetOfferingJobs(PlayerCharacter character)
    {
        var offeringsToAccept = await GetOfferingsToAcceptOrExisting(character);

        List<SeasonalOfferingSchema> doableIncompleteOfferings = [];

        foreach (var selectedOffering in character.OfferingData.SelectedOfferings)
        {
            var matchingOffering = character.OfferingData.Offerings.FirstOrDefault(offering =>
                offering.Id == selectedOffering.OfferingId
            );
            if (matchingOffering is null)
            {
                continue;
            }

            if (
                selectedOffering.Status == SeasonalOfferingStatus.Accepted
                && await CanDoOffering(character, matchingOffering)
            )
            {
                doableIncompleteOfferings.Add(matchingOffering);
            }
        }

        List<SeasonalOfferingSchema> offeringsToProceedWith =
        [
            .. doableIncompleteOfferings,
            .. offeringsToAccept,
        ];

        if (offeringsToProceedWith.Count == 0)
        {
            return [];
        }

        List<CharacterJob> result = [];

        foreach (var offering in offeringsToProceedWith)
        {
            var job = await GetOfferingJob(character, offering);

            result.Add(job);
        }

        return result;
    }

    public static OfferingFavorSchema? GetFavorFromCharacter(PlayerCharacter character, string code)
    {
        return character.OfferingData.ActiveFavors.FirstOrDefault(favor =>
            favor.TargetCode == code
        );
    }

    async Task<bool> CanDoOffering(PlayerCharacter character, SeasonalOfferingSchema offering)
    {
        ItemSchema item = gameState.ItemsDict[offering.Requirement.Code];

        int amountToAcquire = offering.Requirement.Quantity;

        bool pretendWeAreAllowedToTrainSkill = false;

        Skill? skill = SkillService.GetSkillFromName(
            offering.Category.GetDisplayName().FromPascalToSnakeCase()
        );

        // Needed for things like mining/woodcutting, where CanObtainItem currently doesn't respect the "AllowTraining"
        if (
            skill is not null
            && character.GetSkillLevel((Skill)skill) < (item.Craft?.Level ?? item.Level)
        )
        {
            return false;
        }

        // We want to
        // if (item.Craft is not null)
        // {
        //     var potentialCrafter = ItemService.GetCharacterForSkill(
        //         character,
        //         item.Craft.Skill,
        //         item.Craft.Level,
        //         gameState
        //     );

        //     if (potentialCrafter is null)
        //     {
        //         return false;
        //     }

        //     pretendWeAreAllowedToTrainSkill = true;
        // }

        return await character.PlayerActionService.CanObtainItem(
            item,
            amountToAcquire,
            pretendWeAreAllowedToTrainSkill
        );
    }

    async Task<CharacterJob> GetOfferingJob(
        PlayerCharacter character,
        SeasonalOfferingSchema offering
    )
    {
        ItemSchema item = gameState.ItemsDict[offering.Requirement.Code];

        int amountToAcquire = offering.Requirement.Quantity;

        return new ObtainOrFindItem(character, gameState, item.Code, amountToAcquire)
        {
            onJobStartHook = async () =>
            {
                // Allows for "idempotently" running this function many times, to possibly "continue" an offering that already
                // has been accepted previously
                if (
                    !character.OfferingData.SelectedOfferings.Exists(selectedOffering =>
                        selectedOffering.OfferingId == offering.Id
                    )
                )
                {
                    await character.NavigateTo(ContentCode.SeasonalAltar);
                    await character.AcceptOffering(offering.Id);
                }
            },
            onSuccessEndHook = async () =>
            {
                await character.NavigateTo(ContentCode.SeasonalAltar);
                await character.CompleteOffering(offering.Id);
            },
        };
    }

    async Task<List<SeasonalOfferingSchema>> GetOfferingsToAcceptOrExisting(
        PlayerCharacter character
    )
    {
        if (character.OfferingData.SelectedOfferings.Count >= MAX_OFFERINGS)
        {
            return [];
        }

        HashSet<SeasonalOfferingCategory> allCategories =
        [
            .. character.OfferingData.Offerings.Select(offering => offering.Category),
        ];

        HashSet<SeasonalOfferingCategory> availableCategories = GetAllowedCategories(
            allCategories,
            character
        );

        var orderedPossibleOfferingsToAccept = character
            .OfferingData.Offerings.Where(offering =>
                availableCategories.Contains(offering.Category)
            )
            .OrderByDescending(offering => offering.LevelMin);

        List<SeasonalOfferingSchema> offeringsToAccept = [];

        foreach (var offering in orderedPossibleOfferingsToAccept)
        {
            int level = GetCharacterLevelForOffering(character, offering);

            if (await CanDoOffering(character, offering))
            {
                offeringsToAccept.Add(offering);
            }
        }

        return offeringsToAccept;
    }

    HashSet<SeasonalOfferingCategory> GetAllowedCategories(
        IEnumerable<SeasonalOfferingCategory> allCategories,
        PlayerCharacter character
    )
    {
        HashSet<SeasonalOfferingCategory> allowedCategories =
        [
            .. allCategories.Where(category =>
            {
                bool isAllowedCategory = CategoryIsInSkillOrAllowed(character, category);

                if (!isAllowedCategory)
                {
                    return false;
                }

                var matchingOffering = character.OfferingData.SelectedOfferings.FirstOrDefault(
                    selectedOffering => selectedOffering.Category == category
                );

                return matchingOffering is null || matchingOffering.CompletedAt is null;
            }),
        ];

        return allowedCategories;
    }

    bool CategoryIsInSkillOrAllowed(PlayerCharacter character, SeasonalOfferingCategory category)
    {
        if (category == SeasonalOfferingCategory.Combat)
        {
            return true;
        }

        var skill = SkillService.GetSkillFromName(
            category.GetDisplayName().FromPascalToSnakeCase()
        );

        // Should not happen
        if (skill is null)
        {
            return false;
        }

        if (character.Roles.Exists(role => role == skill))
        {
            return true;
        }
        // If we haven't completed the achievement, we will request other characters to make the item for us,
        // so we can hand it in. This means that we will accept offerings, that are outside of our skill range
        //
        // If we have completed the achievement, then we are not as "desperate", and then the favors are basically pointless,
        // since we cannot use them for anything for that character.

        return false;
        // bool achievementIsCompleted =
        //     gameState.Services.AchievementService.IsAchievementCompletedFromCode(AFavoredChampion);

        // // If we have gotten this far, it's not a skill we have or need, so return false
        // if (achievementIsCompleted)
        // {
        //     return false;
        // }

        // // Return true if we find any character with the role, since we have not completed the achievement.
        // return gameState.Characters.Exists(character =>
        //     character.Roles.Exists(role => role == skill)
        // );
    }

    static int GetCharacterLevelForOffering(
        PlayerCharacter character,
        SeasonalOfferingSchema offering
    )
    {
        if (offering.Category == SeasonalOfferingCategory.Combat)
        {
            return character.Schema.Level;
        }
        else
        {
            var skillName = offering.Category.GetDisplayName().FromPascalToSnakeCase();

            return character.GetSkillLevel(skillName);
        }
    }

    static bool HighEnoughLevelForOffering(int level, SeasonalOfferingSchema offering)
    {
        return offering.LevelMin <= level;
    }
}
