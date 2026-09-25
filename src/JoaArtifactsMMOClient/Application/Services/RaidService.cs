using System.Collections;
using Application.ArtifactsApi.Schemas;
using Application.ArtifactsApi.Schemas.Responses;
using Application.Character;
using Application.Errors;
using Application.Records;
using Application.Services.ApiServices;

namespace Application.Services;

public class RaidService
{
    public bool HasDoneItemTask { get; private set; }
    public AccountRequester AccountRequester { get; init; }
    private readonly ILogger<RaidService> Logger;
    private GameState GameState { get; set; }
    public List<RaidSchema> Raids { get; set; } = [];
    public Dictionary<string, RaidSchema> RaidsMonsterDict { get; set; } = [];
    public RaidSchema? RelevantRaidComingUpInSomeHours { get; private set; }

    public RaidService(
        ILogger<RaidService> logger,
        AccountRequester accountRequester,
        GameState gameState
    )
    {
        Logger = logger;
        AccountRequester = accountRequester;
        GameState = gameState;
    }

    public async Task<bool> LoadRaids()
    {
        Logger.LogInformation("Loading raids...");
        bool doneLoading = false;
        List<RaidSchema> raids = [];
        Dictionary<string, RaidSchema> raidSchema = [];
        int pageNumber = 1;

        while (!doneLoading)
        {
            var result = await AccountRequester.GetRaids(pageNumber);

            foreach (var map in result.Data)
            {
                raids.Add(map);
                raidSchema.Add(map.Monster, map);
            }

            if (result.Data.Count == 0)
            {
                doneLoading = true;
            }

            pageNumber++;
        }

        bool isInitialRun = Raids.Count == 0;

        bool raidsHasChanged = NewRaidsAreComingUpInAFewMinutes(Raids, raids);

        RaidsMonsterDict = raidSchema;
        Raids = raids;

        bool raidsHaveChanged = false;

        if (!isInitialRun && raidsHasChanged)
        {
            Logger.LogInformation($"Raids have changed - notifying characters");
            raidsHaveChanged = true;
        }

        RelevantRaidComingUpInSomeHours = Raids.FirstOrDefault(raid =>
        {
            if (!RaidIsStartingInSomeHours(raid))
            {
                return false;
            }

            var levelRange = GameState.GetCharacterLevelRange();

            var matchingMonster = GameState.MonstersDict[raid.Monster];

            // A monster/raid boss can be above max level
            int monsterLevelOrMaxLevel = Math.Min(matchingMonster.Level, PlayerCharacter.MAX_LEVEL);

            return levelRange.Highest >= monsterLevelOrMaxLevel;
        });

        Logger.LogInformation("Loading raids - DONE;");

        return raidsHaveChanged;
    }

    public static bool NewRaidsAreComingUpInAFewMinutes(
        List<RaidSchema> oldRaids,
        List<RaidSchema> newRaids
    )
    {
        if (
            !oldRaids.Exists(RaidIsActive) && newRaids.Exists(RaidIsActive)
            || !oldRaids.Exists(RaidIsStartingInAFewMinutes)
                && newRaids.Exists(RaidIsStartingInAFewMinutes)
        )
        {
            return true;
        }

        return false;
    }

    public static bool RaidIsStartingInAFewMinutes(RaidSchema raid)
    {
        return (raid.NextStartAt - DateTime.UtcNow).TotalSeconds
            <= PlayerAI.START_RAID_IF_WITHIN_SECONDS;
    }

    public static bool RaidIsStartingInSomeHours(RaidSchema raid)
    {
        return (raid.NextStartAt - DateTime.UtcNow).TotalSeconds
            <= PlayerAI.START_RAID_IF_WITHIN_SECONDS;
    }

    public static bool RaidIsActive(RaidSchema raid) => raid.ActiveInstance is not null;
}
