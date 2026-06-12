using System.Reflection.Metadata.Ecma335;
using Application.ArtifactsApi.Schemas;
using Application.ArtifactsApi.Schemas.Responses;
using Application.Character;
using Application.Errors;
using Application.Jobs;
using Microsoft.Extensions.ObjectPool;
using Microsoft.OpenApi.Extensions;
using OneOf;
using OneOf.Types;

namespace Application.Services;

public class NavigationService
{
    public static string SandwhisperIsle = "Sandwhisper Isle";
    public static string ChristmasIsland = "Christmas Island";

    const int COOLDOWN_PER_MAP_SECONDS = 5;

    public static List<string> Islands = new List<string> { SandwhisperIsle, ChristmasIsland };
    public static List<string> UnavailableIslands = new List<string> { ChristmasIsland };

    private Dictionary<string, MapSchema> transitionsToIslandFromMainland = [];
    private Dictionary<string, MapSchema> transitionsFromIslandToMainland = [];

    private readonly GameState gameState;
    private const string Name = "NavigationService";
    private readonly ILogger<NavigationService> Logger;

    private readonly PlayerCharacter character;

    public NavigationService(PlayerCharacter character, GameState gameState)
    {
        this.gameState = gameState;
        this.character = character;

        Logger = AppLogger.loggerFactory.CreateLogger<NavigationService>();

        SetIslandTransitions();
    }

    private void SetIslandTransitions()
    {
        // We assume that all transitions go through the main land, so islands don't have transportation directly to each other.
        foreach (var map in gameState.Maps)
        {
            // All the boats are in the overworld at the moment.
            if (
                map.Interactions.Transition is null
                || map.Interactions.Transition.Layer != MapLayer.Overworld
            )
            {
                continue;
            }

            var comesFromIslandName = Islands.FirstOrDefault(island => island == map.Name);

            MapSchema leadsTo = gameState.MapsDict[map.Interactions.Transition.MapId];

            // Transitions around the same island, e.g. from underground to overworld etc. are irrelevant.
            if (map.Name == leadsTo.Name || map.Layer != leadsTo.Layer)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(comesFromIslandName))
            {
                // It's leading away from the island, e.g. to the main land.
                transitionsFromIslandToMainland.Add(comesFromIslandName, map);
            }
            else if (Islands.Contains(leadsTo.Name))
            {
                // It's a transition to an island - we assume that each island only has one transition there, e.g. one boat goes to Sandwhisper Isle.
                transitionsToIslandFromMainland.Add(leadsTo.Name, map);
            }
        }
    }

    public async Task<OneOf<AppError, None>> NavigateTo(string contentCode)
    {
        var result = GetAllStepsToDestination(contentCode);

        if (result.Value is AppError)
        {
            return result.AsT0;
        }

        await ExecuteNavigations(character, result.AsT1.Steps);

        return new None();
    }

    public OneOf<AppError, NavigationStepsAndRequirements> GetAllStepsToDestination(
        string contentCode
    )
    {
        var maps = gameState.Maps.FindAll(map =>
        {
            bool matchesCode = map.Interactions.Content?.Code == contentCode;

            if (!matchesCode)
            {
                return false;
            }

            if (map.Access.Conditions is not null)
            {
                foreach (var condition in map.Access.Conditions)
                {
                    if (
                        condition.Operator == ItemConditionOperator.AchievementUnlocked
                        && gameState.AccountAchievements.Find(achievement =>
                            achievement.Code == condition.Code
                        )
                            is null
                    )
                    {
                        return false;
                    }
                }
            }

            return true;
        });

        MapSchema? destinationMap = null;
        int closestCost = 0;

        /** Handle navigating across transitions to different layers
         * Handle Sandwhisper Isle - we always need at least 1k gold to cross, and ideally want a recall potion to get back.
         * The transition is also "hardcoded", e.g if you want to navigate from a non-Sandwhisper isle map to a sandwhisper one, we need
         * to go to specific transition points
        **/

        if (maps.Count == 0)
        {
            var map = gameState.EventService.WhereIsEntityActive(contentCode);

            if (map is null)
            {
                throw new Exception($"Could not find map with code {contentCode}");
            }

            destinationMap = map;
        }

        foreach (var map in maps)
        {
            if (destinationMap is null)
            {
                destinationMap = map;
                closestCost = CalculationService.CalculateDistanceToMap(
                    character.Schema.X,
                    character.Schema.Y,
                    map.X,
                    map.Y
                );
                continue;
            }

            int cost = CalculationService.CalculateDistanceToMap(
                character.Schema.X,
                character.Schema.Y,
                map.X,
                map.Y
            );

            if (
                cost < closestCost
                || destinationMap is not null
                    && destinationMap.Access?.Conditions?.Count > 0
                    && (map.Access?.Conditions ?? []).Count == 0
            )
            {
                destinationMap = map;
                closestCost = cost;
            }

            // We are already standing on the map, we won't get any closer :-)
            if (cost == 0)
            {
                break;
            }
        }

        if (destinationMap is null)
        {
            return new AppError(
                $"Could not find closest map to find \"{contentCode}\"",
                ErrorStatus.NotFound
            );
        }

        var currentMap = gameState.MapsDict[character.Schema.MapId];

        // var potionsInInventory = character
        //     .Schema.Inventory.Select(item =>
        //     {
        //         if (!string.IsNullOrWhiteSpace(item.Code))
        //         {
        //             return gameState.ItemsDict[item.Code];
        //         }

        //         return null;
        //     })
        //     .Where(item =>
        //     {
        //         if (item is null || !ItemService.IsTeleportPotion(item))
        //         {
        //             return false;
        //         }

        //         var teleportToMap = gameState.MapsDict[
        //             item.Effects.First(effect => effect.Code == "teleport").Value
        //         ];

        //         if (teleportToMap.Layer == destinationMap.Layer)
        //         {
        //             // Basically we only want to teleport to the island if either the maps are both at the same island, or neither are island maps
        //             if (
        //                 teleportToMap.Name == destinationMap.Name
        //                 || !Islands.Contains(teleportToMap.Name)
        //                     && !Islands.Contains(destinationMap.Name)
        //             )
        //             {
        //                 return true;
        //             }
        //         }

        //         return false;
        //     })
        //     .ToList();

        // if (potionsInInventory.Count > 0)
        // {
        //     potionsInInventory = potionsInInventory;
        // }

        return CalculateStepsToDestination(currentMap, destinationMap);
    }

    public async Task<OneOf<AppError, List<CharacterJob>>> GetJobsNeededForNavigation(
        string contentCode
    )
    {
        var result = GetAllStepsToDestination(contentCode);

        if (result.Value is AppError)
        {
            return result.AsT0;
        }

        var steps = result.AsT1;

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
                if (
                    !await character.PlayerActionService.CanObtainItem(
                        gameState.ItemsDict[itemCondition.Code],
                        amountToObtain
                    )
                )
                {
                    return new AppError(
                        $"GetJobsNeededForNavigation: Cannot obtain item {amountToObtain} x {itemCondition.Code} for {character.Name}"
                    );
                }

                jobs.Add(
                    new ObtainOrFindItem(character, gameState, itemCondition.Code, amountToObtain)
                );
            }
        }

        // TODO: Should acquire gold if needed
        if (steps.GoldRequirement > character.Schema.Gold)
        {
            jobs.Add(
                new WithdrawGold(
                    character,
                    gameState,
                    steps.GoldRequirement - character.Schema.Gold
                )
            );
        }

        return jobs;
    }

    public NavigationStepsAndRequirements CalculateStepsToDestination(
        MapSchema currentMap,
        MapSchema destinationMap
    )
    {
        List<NavigationStep> steps = [];

        int safetyCounter = 0;

        // Implement later
        List<DropSchema> itemRequirements = [];
        int goldRequirement = 0;

        while (currentMap.MapId != destinationMap.MapId)
        {
            var nextSteps = GetNextStepsToDestination(currentMap, destinationMap);

            if (nextSteps.Count == 0)
            {
                throw new AppError(
                    $"Returned an empty list of navigation steps, when trying to find next step from map ID {currentMap.MapId} to {destinationMap.MapId}"
                );
            }

            currentMap = nextSteps.Last().NewMap;

            foreach (var step in nextSteps)
            {
                steps.Add(step);
            }

            safetyCounter++;

            if (safetyCounter > 1000)
            {
                throw new AppError(
                    $"Infinite loop detected while navigating from {currentMap.MapId} to {destinationMap.MapId}"
                );
            }
        }

        foreach (var step in steps)
        {
            List<MapSchema> maps = [step.CurrentMap, step.NewMap];

            foreach (var map in maps)
            {
                List<ItemOrMapCondition> conditions =
                [
                    .. (map.Access?.Conditions ?? []).Union(
                        map.Interactions?.Transition?.Conditions ?? []
                    ),
                ];

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
                        goldRequirement += condition.Value;
                    }
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

    public List<NavigationStep> GetNextStepsToDestination(
        MapSchema currentMap,
        MapSchema destinationMap
    )
    {
        bool goingFromIslandToMainland =
            !Islands.Contains(destinationMap.Name) && Islands.Contains(currentMap.Name);

        bool goingToIslandFromMainland =
            Islands.Contains(destinationMap.Name) && !Islands.Contains(currentMap.Name);

        bool goingFromIslandToIsland =
            Islands.Contains(destinationMap.Name)
            && Islands.Contains(currentMap.Name)
            && destinationMap.Name != currentMap.Name;

        if (goingFromIslandToMainland || goingToIslandFromMainland || goingFromIslandToIsland)
        {
            return GetNextNavigationStepInvolvingIslands(
                goingFromIslandToMainland,
                goingToIslandFromMainland,
                goingFromIslandToIsland,
                currentMap,
                destinationMap
            );
        }

        // We aren't moving between islands, and no transition should be necessary
        if (currentMap.Layer == MapLayer.Overworld && destinationMap.Layer == MapLayer.Overworld)
        {
            return [CreateMoveStep(currentMap, destinationMap)];
        }

        if (currentMap.Layer != MapLayer.Overworld)
        {
            return GetNextNavigationStepIfNotInOverworld(currentMap, destinationMap);
        }

        if (currentMap.Layer == MapLayer.Overworld && destinationMap.Layer != currentMap.Layer)
        {
            return GetNextNavigationStepFromOverworldToOtherLayer(currentMap, destinationMap);
        }

        throw new AppError($"Should never get here");
    }

    List<NavigationStep> GetNextNavigationStepInvolvingIslands(
        bool goingFromIslandToMainland,
        bool goingToIslandFromMainland,
        bool goingFromIslandToIsland,
        MapSchema currentMap,
        MapSchema destinationMap
    )
    {
        // Logger.LogInformation(
        //     $"{Name}: [{character.Name}]: Transitioning involving islands - moving from {currentMap.Name} -> {destinationMap.Name}"
        // );

        if (currentMap.Layer != MapLayer.Overworld)
        {
            MapSchema? closestTransition =
                FindClosestTransition(currentMap, null, false)
                ?? throw new Exception($"Cannot find transition, should not happen");

            // Logger.LogInformation(
            //     $"{Name}: [{character.Name}]: Transitioning involving islands - not in the overworld, going up"
            // );

            var moveStep = CreateMoveStep(currentMap, closestTransition);
            var transitionStep = CreateTransitionStep(gameState.MapsDict, moveStep.NewMap);

            return [moveStep, transitionStep];
        }

        if (goingFromIslandToIsland)
        {
            // Logger.LogInformation(
            //     $"{Name}: [{character.Name}]: Transitioning involving islands - island hopping! We need to go from {currentMap.Name} -> main land first"
            // );
            // We want to go to the mainland, to go to the other island
            goingFromIslandToMainland = true;
        }

        // Going to Sandwhisper
        if (goingToIslandFromMainland)
        {
            // TODO: Should check if we have enough money etc

            var matchingTransition = transitionsToIslandFromMainland[destinationMap.Name];

            var moveStep = CreateMoveStep(currentMap, matchingTransition);
            var transitionStep = CreateTransitionStep(gameState.MapsDict, moveStep.NewMap);
            return [moveStep, transitionStep];
        }

        if (goingFromIslandToMainland)
        {
            var matchingTransition = transitionsFromIslandToMainland[currentMap.Name];
            // We are going back from an island
            // Boat to Sandwhisper Isle

            if (
                matchingTransition.Interactions.Transition!.Conditions.Exists(condition =>
                    condition.Code == "gold"
                )
            )
            {
                // Ghetto recall - find closest mob and intentionally die, so we get ported to spawn

                var closestMonsterResult = FindClosestMonster(matchingTransition);

                if (closestMonsterResult is not null)
                {
                    (MonsterSchema closestMonster, MapSchema closestMonsterMap) =
                        closestMonsterResult.Value;

                    var result = CalculateStepsToDestination(currentMap, closestMonsterMap);

                    async Task afterMoveAction()
                    {
                        while (character.Schema.X != 0 && character.Schema.Y != 0)
                        {
                            await character.Fight();
                        }
                    }

                    var steps = result.Steps;

                    var lastStep = steps.LastOrDefault();

                    // We are already standing here - we make a fake step, so we can attach the afterMoveAction
                    if (lastStep is null)
                    {
                        lastStep = CreateMoveStep(currentMap, destinationMap);
                        lastStep = lastStep with
                        {
                            Move = new Move
                            {
                                X = currentMap.X,
                                Y = currentMap.Y,
                                Layer = currentMap.Layer,
                                ShouldTransition = false,
                                AfterMoveAction = lastStep.Move.AfterMoveAction,
                                TeleportPotionCode = null,
                            },
                        };
                    }

                    lastStep = lastStep with
                    {
                        Move = lastStep.Move with { AfterMoveAction = afterMoveAction },
                        NewMap =
                            gameState.Maps.FirstOrDefault(map =>
                                map.Name.Equals("spawn", StringComparison.CurrentCultureIgnoreCase)
                            )
                            ?? throw new AppError(
                                "Could not find \"spawn\" in maps list - should not happen"
                            ),
                    };

                    // We want to add the new lastStep to the list, instead of the original one
                    if (steps.Count > 0)
                    {
                        steps.RemoveAt(steps.Count - 1);
                    }

                    steps.Add(lastStep);

                    return steps;
                }
            }

            // TODO: Consider using a recall potion if you have one
            var moveStep = CreateMoveStep(currentMap, matchingTransition);
            var transitionStep = CreateTransitionStep(gameState.MapsDict, moveStep.NewMap);
            return [moveStep, transitionStep];
        }

        throw new AppError("One of the bool params should be set");
    }

    List<NavigationStep> GetNextNavigationStepIfNotInOverworld(
        MapSchema currentMap,
        MapSchema destinationMap
    )
    {
        // No matter what, we need to find out whether we are moving within the same "underground cell", or to another one

        // Logger.LogInformation(
        //     $"{Name}: [{character.Name}]: Currently not in the overworld ({currentMap.Layer})"
        // );

        MapSchema? currentClosestTransition = FindClosestTransition(currentMap, null, false)!;

        MapSchema? destinationClosestTransition = FindClosestTransition(
            destinationMap,
            null,
            false
        )!;

        if (currentMap.Layer == destinationMap.Layer)
        {
            // We are in the same cell, the transitions are the same
            if (currentClosestTransition.MapId == destinationClosestTransition.MapId)
            {
                // Logger.LogInformation(
                //     $"{Name}: [{character.Name}]: Currently not in the overworld ({currentMap.Layer}), but moving inside the same \"cell\" - no need to transition"
                // );
                return [CreateMoveStep(currentMap, destinationMap)];
            }
        }

        // Logger.LogInformation(
        //     $"{Name}: [{character.Name}]: Currently not in the overworld ({currentMap.Layer}) - need to transition first - moving to ({currentClosestTransition.X}, {currentClosestTransition.Y})"
        // );
        // Don't care if we are going from e.g. underground -> overworld or interior, we need to go to the overworld first
        var moveStep = CreateMoveStep(currentMap, currentClosestTransition);
        var transitionStep = CreateTransitionStep(gameState.MapsDict, moveStep.NewMap);

        return [moveStep, transitionStep];
    }

    List<NavigationStep> GetNextNavigationStepFromOverworldToOtherLayer(
        MapSchema currentMap,
        MapSchema destinationMap
    )
    {
        /* We know that normal overworld -> other layer transitions are directly above their counterpart, e.g. if the door to a house is on position 1,1,
           then when we transition through that door, we will be on 1,1 in the interior or underground layer. So we can just calculate the distance from where
           we are, and to the destination map, and find the closest transition point from the destination map. It's not entirely bullet-proof, but it should be
           good enough.
        */
        MapSchema? closestTransitionNotInTheOverworld = FindClosestTransition(
            destinationMap,
            MapLayer.Overworld,
            false
        );

        if (closestTransitionNotInTheOverworld is null)
        {
            throw new AppError(
                $"Could not find transition to get to {destinationMap.Name} - x = {destinationMap.X} y = {destinationMap.Y}",
                ErrorStatus.NotFound
            );
        }

        if (closestTransitionNotInTheOverworld.Access?.Conditions?.Count > 0)
        {
            // Logger.LogDebug(
            //     $"Condition to go to {destinationMap.MapId} ({destinationMap.X}, {destinationMap.Y})"
            // );
        }

        var transitionToUse = gameState.MapsDict[
            closestTransitionNotInTheOverworld.Interactions.Transition!.MapId
        ];

        // Logger.LogInformation(
        //     $"{Name}: [{character.Name}]: Going inside from the overworld - moving to ({transitionToUse.X}, {transitionToUse.Y}, {transitionToUse.Layer.GetDisplayName()}) and transitioning"
        // );

        var moveStep = CreateMoveStep(currentMap, transitionToUse);

        var transitionStep = CreateTransitionStep(gameState.MapsDict, moveStep.NewMap);

        return [moveStep, transitionStep];
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

                return gameState.MonstersDict.GetValueOrNull(map.Interactions.Content.Code)
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

        return (gameState.MonstersDict[closestMonsterCode], monsterMap);
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
                TeleportPotionCode = null,
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
                TeleportPotionCode = null,
            },
            NewMap = destinationMap,
        };
    }

    public static async Task ExecuteMove(PlayerCharacter character, Move move)
    {
        if (move.ShouldTransition)
        {
            await character.Transition();
        }
        else
        {
            await character.Move(move.X, move.Y);
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

    public static Move? GetPotionMove(
        PlayerCharacter character,
        GameState gameState,
        MapSchema currentMap,
        MapSchema destinationMap
    )
    {
        var potionsInInventory = character
            .Schema.Inventory.Select(item =>
            {
                if (!string.IsNullOrWhiteSpace(item.Code))
                {
                    return gameState.ItemsDict[item.Code];
                }

                return null;
            })
            .Where(item =>
            {
                if (item is null || !ItemService.IsTeleportPotion(item))
                {
                    return false;
                }

                var teleportToMap = gameState.MapsDict[
                    item.Effects.First(effect => effect.Code == "teleport").Value
                ];

                if (teleportToMap.Layer == destinationMap.Layer)
                {
                    // Basically we only want to teleport to the island if either the maps are both at the same island, or neither are island maps
                    if (
                        teleportToMap.Name == destinationMap.Name
                        || !Islands.Contains(teleportToMap.Name)
                            && !Islands.Contains(destinationMap.Name)
                    )
                    {
                        return true;
                    }
                }

                return false;
            })
            .ToList();

        // if (potionsInInventory.Count > 0)
        // {
        //     potionsInInventory = potionsInInventory;
        // }

        return null;
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

    public required string? TeleportPotionCode { get; init; }

    /** An action that can be run, which will take the character to the destinationMap of a move. E.g. using a recall potion*/
    public Func<Task>? AfterMoveAction { get; init; }
}

public record NavigationStepsAndRequirements
{
    public required List<NavigationStep> Steps { get; set; } = [];
    public required List<DropSchema> ItemRequirements { get; set; } = [];
    public required int GoldRequirement { get; set; } = 0;
}
