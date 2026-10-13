using System.Net;
using System.Runtime.CompilerServices;
using Application.ArtifactsApi.Schemas;
using Application.Character;
using Application.Errors;
using Application.Jobs;
using Microsoft.OpenApi.Extensions;
using OneOf;
using OneOf.Types;

namespace Application.Services;

public class NavigationService
{
    public const string ChristmasIsland = "Christmas Island";
    public const string SandwhisperIsle = "Sandwhisper Isle";

    public readonly List<string> Islands = [SandwhisperIsle, ChristmasIsland];

    const int COOLDOWN_PER_MAP_SECONDS = 5;
    const int SECONDS_SAVED_TO_USE_TELEPORT_POTION = 45;

    private readonly GameState gameState;
    private const string Name = "NavigationService";
    private readonly ILogger<NavigationService> Logger;

    private readonly PlayerCharacter character;

    public PathfindingService PathfindingService { get; private set; }

    public NavigationService(PlayerCharacter character, GameState gameState)
    {
        this.gameState = gameState;
        this.character = character;

        Logger = AppLogger.loggerFactory.CreateLogger<NavigationService>();
        PathfindingService = PathfindingService.GetInstance(this.gameState.Maps);
    }

    public async Task<OneOf<AppError, NavigateToResult>> NavigateTo(
        string contentCode,
        MapSchema? currentMap = null,
        bool dryRun = false,
        bool allowObtaining = false
    )
    {
        currentMap ??= gameState.MapsDict[character.Schema.MapId];

        var result = GetAllStepsToDestination(contentCode, currentMap);

        if (result.Value is AppError)
        {
            return result.AsT0;
        }

        /**
        ** If we are at the bank, do some stuff before leaving it.
        ** It's a bit dirty, but what the hell.
        */

        if (!dryRun)
        {
            await character.PlayerActionService.BeforeMoveHook(contentCode, currentMap);
        }

        foreach (var navigationSteps in result.AsT1)
        {
            var jobsResult = await InnerGetJobsNeededForNavigation(
                contentCode,
                navigationSteps,
                allowObtaining
            );

            if (jobsResult.IsT0)
            {
                continue;
            }

            if (!dryRun)
            {
                // Goes against design principles a bit, but it's the most practical to just run the jobs here
                foreach (var job in jobsResult.AsT1)
                {
                    var jobResult = await job.StartJobAsync();

                    if (jobResult.IsT0)
                    {
                        return jobResult.AsT0;
                    }
                }

                await ExecuteNavigations(character, navigationSteps.Steps);
            }

            return new NavigateToResult { NeededJobs = jobsResult.AsT1 };
        }

        return new AppError($"Could not navigate to {contentCode} for character {character.Name}");
    }

    public async ValueTask<bool> CanNavigateTo(string contentCode, MapSchema? currentMap = null)
    {
        // var result = GetAllStepsToDestination(contentCode, currentMap);

        // if (result.IsT0)
        // {
        //     return false;
        // }

        // var jobsResult = await GetJobsNeededForNavigation(contentCode, currentMap);

        // if (jobsResult.IsT0)
        // {
        //     return false;
        // }

        var navigationOutcome = await NavigateTo(contentCode, null, true);

        return navigationOutcome.IsT0;

        // return true;
    }

    public OneOf<AppError, List<NavigationStepsAndRequirements>> GetAllStepsToDestination(
        string contentCode,
        MapSchema? currentMap = null
    )
    {
        currentMap ??= gameState.MapsDict[character.Schema.MapId];

        var possibleRaidCode = gameState
            .Services.RaidService.RaidsMonsterDict.GetValueOrNull(contentCode)
            ?.Code;

        if (possibleRaidCode is not null)
        {
            contentCode = possibleRaidCode;
        }

        var maps = GetMapsForContent(gameState, contentCode)
            .Select(map =>
            {
                int distance = CalculationService.CalculateDistanceToMap(
                    currentMap.X,
                    currentMap.Y,
                    map.X,
                    map.Y
                );

                return new MapWithDistance { Map = map, Distance = distance };
            })
            .OrderBy(
                (map) =>
                {
                    return map.Distance;
                }
            )
            .ToList();

        if (maps.Count == 0)
        {
            var map = gameState.Services.EventService.WhereIsEntityActive(contentCode);

            if (map is null)
            {
                return new AppError($"Could not find map with code {contentCode}");
            }
        }

        List<(
            MapWithDistance mapWithDistance,
            NavigationStepsAndRequirements stepsWithRequirements,
            NavigationStepsAndRequirements? stepsWithPotion
        )> MapWithMoves = [];

        foreach (var map in maps)
        {
            NavigationStepsAndRequirements stepsWithRequirements = CalculateStepsToDestination(
                currentMap,
                map.Map
            );

            var stepsWithPotion = GetPotionOrOtherCheekyMove(
                character,
                gameState,
                stepsWithRequirements,
                currentMap,
                map.Map
            );

            MapWithMoves.Add((mapWithDistance: map, stepsWithRequirements, stepsWithPotion));
        }

        List<NavigationStepsAndRequirements> sortedMoveCandidates =
        [
            .. MapWithMoves
                .OrderBy(mapWithMove => mapWithMove.stepsWithPotion is not null ? 0 : 1)
                .OrderBy(mapWithMove =>
                {
                    if (mapWithMove.stepsWithPotion is not null)
                    {
                        return mapWithMove.stepsWithPotion.ItemRequirements.Count
                            + mapWithMove.stepsWithPotion.GoldRequirement;
                    }

                    return mapWithMove.stepsWithRequirements.ItemRequirements.Count
                        + mapWithMove.stepsWithRequirements.GoldRequirement;
                })
                .ThenBy(mapWithMove => mapWithMove.mapWithDistance.Distance)
                .Select(mapWithMove =>
                {
                    return mapWithMove.stepsWithPotion is not null
                        ? mapWithMove.stepsWithPotion
                        : mapWithMove.stepsWithRequirements;
                }),
        ];

        if (sortedMoveCandidates.Count == 0)
        {
            return new AppError(
                $"Could not find closest map to find \"{contentCode}\"",
                ErrorStatus.NotFound
            );
        }

        return sortedMoveCandidates;
    }

    static List<MapSchema> GetMapsForContent(GameState gameState, string contentCode)
    {
        var maps = gameState.Maps.FindAll(map =>
        {
            bool matchesCode = map.Interactions.Content?.Code == contentCode;

            if (!matchesCode)
            {
                return false;
            }

            List<ItemOrMapCondition> conditions = [.. map.Access.Conditions ?? []];

            foreach (var condition in conditions)
            {
                if (
                    condition.Operator == ItemConditionOperator.AchievementUnlocked
                    && !gameState.Services.AchievementService.AccountAchievements.Exists(
                        achievement => achievement.Code == condition.Code
                    )
                )
                {
                    return false;
                }
            }

            return true;
        });

        return maps;
    }

    public async Task<OneOf<AppError, List<CharacterJob>>> GetJobsNeededForNavigation(
        string contentCode,
        MapSchema? currentMap = null
    )
    {
        var result = await NavigateTo(contentCode, currentMap, true, true);

        if (result.IsT0)
        {
            return result.AsT0;
        }

        return result.AsT1.NeededJobs;
    }

    async Task<OneOf<AppError, List<CharacterJob>>> InnerGetJobsNeededForNavigation(
        string contentCode,
        NavigationStepsAndRequirements steps,
        bool allowObtaining
    )
    {
        var bankItems = await gameState.Services.BankItemCache.GetBankItems(character);
        var bankDetails = await gameState.Services.BankItemCache.GetBankDetails();

        List<CharacterJob> jobs = [];

        foreach (var itemCondition in steps.ItemRequirements)
        {
            int amountOfItemOnCharacter =
                character
                    .GetEquippedItemOrInInventory(itemCondition.Code)
                    ?.Sum(item => item.equipmentSlot.Quantity) ?? 0;

            int amountToObtain =
                itemCondition.Quantity > amountOfItemOnCharacter
                    ? itemCondition.Quantity - amountOfItemOnCharacter
                    : 0;

            if (amountToObtain > 0)
            {
                if (allowObtaining)
                {
                    if (
                        await character.PlayerActionService.CanObtainItem(
                            gameState.ItemsDict[itemCondition.Code],
                            amountToObtain,
                            false,
                            [contentCode]
                        )
                    )
                    {
                        jobs.Add(
                            new ObtainOrFindItem(
                                character,
                                gameState,
                                itemCondition.Code,
                                amountToObtain,
                                [contentCode]
                            )
                        );
                    }
                    else
                    {
                        return new AppError(
                            $"GetJobsNeededForNavigation: Cannot obtain item {amountToObtain} x {itemCondition.Code} for {character.Name}"
                        );
                    }
                }
                else
                {
                    if (
                        bankItems.Exists(bankItem =>
                            bankItem.Code == itemCondition.Code
                            && bankItem.Quantity >= amountToObtain
                        )
                    )
                    {
                        jobs.Add(
                            new WithdrawItem(
                                character,
                                gameState,
                                itemCondition.Code,
                                amountToObtain
                            )
                        );
                    }
                    else
                    {
                        return new AppError(
                            $"GetJobsNeededForNavigation: Cannot withdraw item {amountToObtain} x {itemCondition.Code} for {character.Name}"
                        );
                    }
                }
            }
        }

        // TODO: Should acquire gold if needed

        if (steps.GoldRequirement > character.Schema.Gold)
        {
            if (bankDetails.Gold + character.Schema.Gold >= steps.GoldRequirement)
            {
                jobs.Add(
                    new WithdrawGold(
                        character,
                        gameState,
                        steps.GoldRequirement - character.Schema.Gold
                    )
                );
            }
            else
            {
                return new AppError(
                    $"GetJobsNeededForNavigation: Cannot obtain gold {steps.GoldRequirement} for {character.Name} to withdraw"
                );
            }
        }

        return jobs;
    }

    public NavigationStepsAndRequirements CalculateStepsToDestination(
        MapSchema currentMap,
        MapSchema destinationMap
    )
    {
        List<DropSchema> itemRequirements = [];
        int goldRequirement = 0;

        var destinationZone = PathfindingService.MapIdToZoneMap[destinationMap.MapId];

        var zoneHopsRequiredResult = PathfindingService.GetZonesToDestination(
            PathfindingService.MapIdToZoneMap[currentMap.MapId],
            destinationZone
        );

        if (zoneHopsRequiredResult.IsT0)
        {
            throw zoneHopsRequiredResult.AsT0;
        }

        var zoneHopsRequired = zoneHopsRequiredResult.AsT1;

        Queue<MapSchema> transitionMaps = [];

        for (int i = 0; i < zoneHopsRequired.Count - 1; i++)
        {
            Zone thisZone = zoneHopsRequired[i];

            Zone nextZone = zoneHopsRequired[i + 1];

            MapSchema transitionToNextZone = thisZone.TransitionMaps.First(transition =>
            {
                var destinationTransitionMapId = transition.Interactions.Transition!.MapId;

                return nextZone.TransitionMaps.Exists(transition =>
                    transition.MapId == destinationTransitionMapId
                );
            });

            transitionMaps.Enqueue(transitionToNextZone);
        }

        List<NavigationStep> steps = [];

        while (currentMap.MapId != destinationMap.MapId)
        {
            var thereIsTransition = transitionMaps.TryDequeue(
                out MapSchema? nextTransitionInSameZoneMap
            );

            /**
            ** We might be moving in the same zone, so there were no zone hops at all. Then we just skip moving to the transition.
            */
            if (nextTransitionInSameZoneMap is not null)
            {
                var moveStep = CreateMoveStep(currentMap, nextTransitionInSameZoneMap);

                var transitionStep = CreateTransitionStep(gameState.MapsDict, moveStep.NewMap);

                steps.AddRange([moveStep, transitionStep]);

                currentMap = transitionStep.NewMap;
            }

            if (PathfindingService.MapIdToZoneMap[currentMap.MapId].Id == destinationZone.Id)
            {
                var moveToDestination = CreateMoveStep(currentMap, destinationMap);
                steps.Add(moveToDestination);

                currentMap = moveToDestination.NewMap;
            }
        }

        var conditions = steps
            .SelectMany(
                (step, index) =>
                {
                    bool isFirstStep = index == 0;

                    bool isLastStep = steps.Count - 1 == index;

                    // If this is the first step, we are already standing here - should have access
                    List<ItemOrMapCondition> stepConditions = isFirstStep
                        ? []
                        : [.. step.CurrentMap.Access?.Conditions ?? []];

                    if (step.Move.ShouldTransition)
                    {
                        // If we are planning to transition to the next map, we should save the requirements for getting there
                        if (step.CurrentMap.Interactions.Transition?.Conditions is not null)
                        {
                            stepConditions =
                            [
                                .. stepConditions.Union(
                                    step.CurrentMap.Interactions.Transition.Conditions
                                ),
                            ];
                        }
                    }

                    if (isLastStep && step.NewMap.Access.Conditions?.Count > 0)
                    {
                        stepConditions = [.. stepConditions.Union(step.NewMap.Access.Conditions)];
                    }

                    isFirstStep = false;

                    return stepConditions;
                }
            )
            .ToList();

        foreach (var condition in conditions)
        {
            if (condition.Operator == ItemConditionOperator.HasItem)
            {
                itemRequirements.Add(
                    new DropSchema { Code = condition.Code, Quantity = condition.Value }
                );
            }
            else if (condition.Operator == ItemConditionOperator.Cost)
            {
                if (condition.Code == "gold")
                {
                    goldRequirement += condition.Value;
                }
                else
                {
                    itemRequirements.Add(
                        new DropSchema { Code = condition.Code, Quantity = condition.Value }
                    );
                }
            }
        }

        return new NavigationStepsAndRequirements
        {
            Steps = steps,
            ItemRequirements = itemRequirements,
            GoldRequirement = goldRequirement,
        };
    }

    public static async Task ExecuteNavigations(
        PlayerCharacter character,
        IList<NavigationStep> steps
    )
    {
        foreach (var step in steps)
        {
            await ExecuteMove(character, step.Move);
        }
    }

    public MapSchema? FindClosestTransition(
        MapSchema currentMap,
        MapLayer? destinationLayer,
        bool mustGoToOtherLayer
    )
    {
        MapSchema? closestTransition = null;
        int closestCostToTransition = 0;

        foreach (var map in gameState.Maps)
        {
            if (map.Layer != currentMap.Layer || map.Interactions.Transition is null)
            {
                continue;
            }

            if (
                (destinationLayer is null || mustGoToOtherLayer)
                && map.Interactions.Transition.Layer == map.Layer
            )
            {
                continue;
            }

            if (
                destinationLayer is not null
                && map.Interactions.Transition.Layer != destinationLayer
            )
            {
                continue;
            }

            int cost = CalculationService.CalculateDistanceToMap(
                currentMap.X,
                currentMap.Y,
                map.X,
                map.Y
            );

            if (closestTransition is null || cost < closestCostToTransition)
            {
                closestTransition = map;
                closestCostToTransition = cost;
            }
        }

        return closestTransition;
    }

    public (MonsterSchema monster, MapSchema map)? FindClosestMonster(MapSchema currentMap)
    {
        List<MapSchema> monsterMaps =
        [
            .. gameState.Maps.Where(map =>
            {
                if (map.Interactions.Content is null)
                {
                    return false;
                }

                return gameState.AvailableMonstersDict.GetValueOrNull(map.Interactions.Content.Code)
                    is not null;
            }),
        ];

        monsterMaps.Sort(
            (a, b) =>
            {
                int aDistance = CalculationService.CalculateDistanceToMap(
                    a.X,
                    a.Y,
                    currentMap.X,
                    currentMap.Y
                );
                int bDistance = CalculationService.CalculateDistanceToMap(
                    b.X,
                    b.Y,
                    currentMap.X,
                    currentMap.Y
                );

                return aDistance - bDistance;
            }
        );

        var monsterMap = monsterMaps.FirstOrDefault();

        if (monsterMap is null || monsterMap.Interactions.Content is null)
        {
            return null;
        }

        var closestMonsterCode = monsterMap.Interactions.Content.Code;

        return (gameState.AvailableMonstersDict[closestMonsterCode], monsterMap);
    }

    public static NavigationStep CreateMoveStep(
        MapSchema currentMap,
        MapSchema destinationMap,
        Func<Task>? MoveAction = null
    )
    {
        return new NavigationStep
        {
            CurrentMap = currentMap,
            Move = new Move
            {
                X = destinationMap.X,
                Y = destinationMap.Y,
                Layer = destinationMap.Layer,
                ShouldTransition = false,
                AfterMoveAction = MoveAction,
            },
            NewMap = destinationMap,
        };
    }

    public static NavigationStep CreateTransitionStep(
        Dictionary<int, MapSchema> MapsDict,
        MapSchema currentMap,
        Func<Task>? MoveAction = null
    )
    {
        if (currentMap.Interactions?.Transition is null)
        {
            throw new AppError(
                $"Could not find transition on current map x: {currentMap.X} y: {currentMap.Y}: layer: {currentMap.Layer.GetDisplayName()}"
            );
        }
        var destinationMap = MapsDict[currentMap.Interactions.Transition.MapId];

        return new NavigationStep
        {
            CurrentMap = currentMap,
            Move = new Move
            {
                X = currentMap.X,
                Y = currentMap.Y,
                Layer = currentMap.Layer,
                ShouldTransition = true,
                AfterMoveAction = MoveAction,
            },
            NewMap = destinationMap,
        };
    }

    public static async Task ExecuteMove(PlayerCharacter character, Move move)
    {
        await character.Move(move.X, move.Y);

        if (move.ShouldTransition)
        {
            await character.Transition();
        }

        if (move.AfterMoveAction is not null)
        {
            await move.AfterMoveAction();
        }
    }

    public static int CalculateMovementCostSeconds(MapSchema mapA, MapSchema mapB)
    {
        int cost = CalculationService.CalculateDistanceToMap(mapA.X, mapA.Y, mapB.X, mapB.Y);

        return cost * COOLDOWN_PER_MAP_SECONDS;
    }

    public NavigationStepsAndRequirements? GetPotionOrOtherCheekyMove(
        PlayerCharacter character,
        GameState gameState,
        NavigationStepsAndRequirements currentToDestinationSteps,
        MapSchema currentMap,
        MapSchema destinationMap
    )
    {
        int currentToDestinationDistance = GetDistanceFromNavigationSteps(
            currentToDestinationSteps
        );

        int secondsUsedWithCurrentPath = GetSecondsToMoveToMap(currentToDestinationDistance);

        // Don't even bother considering teleport potions then
        if (
            secondsUsedWithCurrentPath < SECONDS_SAVED_TO_USE_TELEPORT_POTION
            && currentToDestinationSteps.GoldRequirement == 0
            && currentToDestinationSteps.ItemRequirements.Count == 0
        )
        {
            return null;
        }

        NavigationStepsAndRequirements? result = null;

        var eligibleTeleportPotions = character
            .Schema.Inventory.Where(item => !string.IsNullOrWhiteSpace(item.Code))
            .Select(item =>
            {
                var matchingItem = gameState.ItemsDict[item.Code];

                (ItemSchema Item, SimpleEffectSchema? TeleportEffect) result = (
                    gameState.ItemsDict[item.Code],
                    matchingItem.Effects.FirstOrDefault(effect => effect.Code == Effect.Teleport)
                );

                return result;
            })
            .Where(item => item.TeleportEffect is not null)
            .ToList();

        if (eligibleTeleportPotions.Count > 0)
        {
            var potionsWithDistances = eligibleTeleportPotions
                .Select(item =>
                {
                    var teleportToMap = gameState.MapsDict[item.TeleportEffect!.Value];

                    var resultWithTeleportPotion = CalculateStepsToDestination(
                        teleportToMap,
                        destinationMap
                    );

                    int teleportToDestinationDistance = GetDistanceFromNavigationSteps(
                        resultWithTeleportPotion
                    );

                    return (item, resultWithTeleportPotion, teleportToDestinationDistance);
                })
                .ToList();

            potionsWithDistances.Sort(
                (a, b) => a.teleportToDestinationDistance - b.teleportToDestinationDistance
            );

            var bestCandidate = potionsWithDistances.FirstOrDefault();

            var secondsWithPotion = GetSecondsToMoveToMap(
                bestCandidate.teleportToDestinationDistance
            );

            if (
                secondsWithPotion + SECONDS_SAVED_TO_USE_TELEPORT_POTION
                    < secondsUsedWithCurrentPath
                || currentToDestinationSteps.GoldRequirement
                    > bestCandidate.resultWithTeleportPotion.GoldRequirement
            )
            {
                var teleportToMap = gameState.MapsDict[bestCandidate.item.TeleportEffect!.Value];

                var resultWithTeleportPotion = bestCandidate.resultWithTeleportPotion;

                // We are already standing here - we make a fake step, so we can attach the afterMoveAction
                var usePotionStep = CreateMoveStep(currentMap, destinationMap);

                usePotionStep = usePotionStep with
                {
                    Move = new Move
                    {
                        X = currentMap.X,
                        Y = currentMap.Y,
                        Layer = currentMap.Layer,
                        ShouldTransition = false,
                        AfterMoveAction = async () =>
                        {
                            int secondsSaved =
                                GetSecondsToMoveToMap(bestCandidate.teleportToDestinationDistance)
                                - GetSecondsToMoveToMap(currentToDestinationDistance);

                            Logger.LogInformation(
                                "[{name}]: Using {code} to spend {potionSeconds} seconds instead of {currentSeconds} seconds moving from {currentMap} to {destinationMap} (teleport location is {teleportLocation})",
                                character.Name,
                                bestCandidate.item.Item.Code,
                                secondsWithPotion,
                                secondsUsedWithCurrentPath,
                                $"(X = {currentMap.X}, Y = {currentMap.Y})",
                                $"(X = {destinationMap.X}, Y = {destinationMap.Y})",
                                $"(X = {teleportToMap.X}, Y = {teleportToMap.Y})"
                            );
                            await character.UseItem(bestCandidate.item.Item.Code, 1);
                        },
                    },
                    NewMap = teleportToMap with { },
                };

                resultWithTeleportPotion.Steps =
                [
                    .. resultWithTeleportPotion.Steps.Prepend(usePotionStep),
                ];

                return resultWithTeleportPotion;
            }
        }
        else if (
            (
                currentToDestinationSteps.GoldRequirement > 0
                || currentToDestinationSteps.ItemRequirements.Count > 0
            )
            // && Islands.Contains(currentMap.Name)
            && currentMap.Name == SandwhisperIsle
        )
        {
            // Evaluate ghetto recall - go fight a monster, until the character dies and is sent back to spawn
            var spawn = gameState.Maps.Find(map =>
                map.X == 0 && map.Y == 0 && map.Layer == MapLayer.Overworld
            )!;

            // We are already standing here - we make a fake step, so we can attach the beforeMoveAction
            var ghettoRecallStep = CreateMoveStep(currentMap, destinationMap);

            ghettoRecallStep = ghettoRecallStep with
            {
                Move = new Move
                {
                    X = currentMap.X,
                    Y = currentMap.Y,
                    Layer = currentMap.Layer,
                    ShouldTransition = false,
                    AfterMoveAction = async () =>
                    {
                        var monsterCode = FindClosestMonster(
                            gameState.MapsDict[character.Schema.MapId]
                        )!.Value.monster.Code;

                        while (character.Schema.MapId != spawn.MapId)
                        {
                            await character.NavigateTo(monsterCode);

                            await character.Fight();
                        }

                        Logger.LogInformation(
                            "[{name}]: Attempting to ghetto recall - going to fight {monsterCode} until I end up at spawn",
                            character.Name,
                            monsterCode
                        );
                    },
                },
                NewMap = spawn with { },
            };

            NavigationStepsAndRequirements resultWithGhettoRecall = CalculateStepsToDestination(
                spawn,
                destinationMap
            );

            resultWithGhettoRecall.Steps =
            [
                .. resultWithGhettoRecall.Steps.Prepend(ghettoRecallStep),
            ];

            return resultWithGhettoRecall;
        }
        return result;
    }

    public int GetSecondsToMoveToMap(int distance)
    {
        return distance * COOLDOWN_PER_MAP_SECONDS;
    }

    public static int GetDistanceFromNavigationSteps(NavigationStepsAndRequirements steps)
    {
        return steps.Steps.Sum(step =>
            CalculationService.CalculateDistanceToMap(
                step.CurrentMap.X,
                step.CurrentMap.Y,
                step.Move.X,
                step.Move.Y
            )
        );
    }
}

public record NavigationStep
{
    public required MapSchema CurrentMap { get; init; }
    public required Move Move { get; init; }
    public required MapSchema NewMap { get; init; }
}

public record Move
{
    public required int X { get; init; }
    public required int Y { get; init; }
    public required MapLayer Layer { get; init; }

    public required bool ShouldTransition { get; init; }

    /** An action that can be run, which will take the character to the destinationMap of a move. E.g. using a recall potion*/
    public Func<Task>? AfterMoveAction { get; init; }
}

public record NavigationStepsAndRequirements
{
    public required List<NavigationStep> Steps { get; set; } = [];
    public required List<DropSchema> ItemRequirements { get; set; } = [];
    public required int GoldRequirement { get; set; } = 0;
}

public record MapWithDistance
{
    public required MapSchema Map { get; init; }
    public required int Distance { get; init; }
}

public record NavigateToResult
{
    public required List<CharacterJob> NeededJobs { get; init; }
}
