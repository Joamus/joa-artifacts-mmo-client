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

    public async Task<bool> LoadRaids(bool isInitialRun)
    {
        Logger.LogInformation("Loading raids...");
        bool doneLoading = false;
        List<RaidSchema> raids = [];
        Dictionary<string, RaidSchema> raidMonstersDict = [];
        int pageNumber = 1;

        while (!doneLoading)
        {
            var result = await AccountRequester.GetRaids(pageNumber);

            foreach (var raid in result.Data)
            {
                // // FOR TESTING
                // if (raid.Code == "enchanted_fairy")
                // {
                //     raid.NextStartAt = DateTime.UtcNow.AddMinutes(2);
                // }

                raids.Add(raid);
                raidMonstersDict.Add(raid.Monster, raid);
            }

            if (result.Data.Count == 0)
            {
                doneLoading = true;
            }

            pageNumber++;
        }

        bool raidsHaveChanged = !isInitialRun && NewRaidsAreComingUpInAFewMinutes(Raids, raids);

        RaidsMonsterDict = raidMonstersDict;
        Raids = raids;

        if (raidsHaveChanged)
        {
            Logger.LogInformation($"Raids have changed - notifying characters");
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
            int monsterLevelToCompareTo = MonsterService.GetCappedLevel(matchingMonster.Level);

            return levelRange.Highest >= monsterLevelToCompareTo;
        });

        Logger.LogInformation(
            "Loading raids - DONE. Relevant raid coming up: {raid}",
            string.Join(", ", RelevantRaidComingUpInSomeHours?.Code ?? "n/a")
        );

        return raidsHaveChanged;
    }

    public static bool NewRaidsAreComingUpInAFewMinutes(
        List<RaidSchema> oldRaids,
        List<RaidSchema> newRaids
    )
    {
        return (!oldRaids.Exists(RaidIsActive) && newRaids.Exists(RaidIsActive))
            || (
                !oldRaids.Exists(RaidIsStartingInAFewMinutes)
                && newRaids.Exists(RaidIsStartingInAFewMinutes)
            );
    }

    public static bool RaidIsStartingInAFewMinutes(RaidSchema raid)
    {
        return (raid.NextStartAt - DateTime.UtcNow).TotalSeconds
            <= PlayerAI.START_RAID_IF_WITHIN_SECONDS;
    }

    public static bool RaidIsStartingInSomeHours(RaidSchema raid)
    {
        return (raid.NextStartAt - DateTime.UtcNow).TotalHours
            <= PlayerAI.RAID_STARTING_WITHIN_SOME_HOURS;
    }

    public static bool RaidIsActive(RaidSchema raid) => raid.ActiveInstance is not null;
}
