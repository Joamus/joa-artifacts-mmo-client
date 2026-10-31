using System.Collections.Frozen;
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

        return await character.PlayerActionService.CanObtainItem(item, amountToAcquire, false);
    }

    async Task<CharacterJob> GetOfferingJob(
        PlayerCharacter character,
        SeasonalOfferingSchema offering
    )
    {
        ItemSchema item = gameState.ItemsDict[offering.Requirement.Code];

        int amountToAcquire = offering.Requirement.Quantity;

        return new ObtainItem(character, gameState, item.Code, amountToAcquire)
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

        HashSet<SeasonalOfferingCategory> availableCategories =
        [
            .. allCategories.Where(category =>
                !character.OfferingData.SelectedOfferings.Exists(selectedOffering =>
                    selectedOffering.Category == category
                    && (
                        category == SeasonalOfferingCategory.Combat
                        || character.Roles.Exists(role =>
                            // Categories are basically just skills, but with "combat" added
                            role.GetDisplayName() == category.GetDisplayName()
                        )
                    )
                )
            ),
        ];

        var orderedPossibleOfferingsToAccept = character
            .OfferingData.Offerings.Where(offering =>
                availableCategories.Contains(offering.Category)
            )
            .OrderByDescending(offering => offering.LevelMin);

        List<SeasonalOfferingSchema> offeringsToAccept = [];

        foreach (var offering in orderedPossibleOfferingsToAccept)
        {
            int level = GetCharacterLevelForOffering(character, offering);

            if (
                !HighEnoughLevelForOffering(level, offering)
                || !await CanDoOffering(character, offering)
            )
            {
                continue;
            }

            offeringsToAccept.Add(offering);
        }

        return offeringsToAccept;
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
