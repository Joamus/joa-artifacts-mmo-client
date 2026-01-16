using Application.ArtifactsApi.Schemas;
using Application.Character;
using Application.Errors;
using OneOf;
using OneOf.Types;

namespace Application.Services;

public class NavigationService
{
    public static string SandwhisperIsle = "Sandwhisper Isle";
    public static string ChristmasIsland = "Christmas Island";

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

    public async Task<OneOf<AppError, None>> NavigateTo(string code)
    {
        // // We don't know what it is, but it might be an item we wish to get

        var maps = gameState.Maps.FindAll(map =>
        {
            bool matchesCode = map.Interactions.Content?.Code == code;

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
            var map = gameState.EventService.WhereIsEntityActive(code);

            if (map is null)
            {
                throw new Exception($"Could not find map with code {code}");
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

            if (cost < closestCost)
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
                $"Could not find closest map to find \"{code}\"",
                ErrorStatus.NotFound
            );
        }

        while (
            character.Schema.X != destinationMap.X
            || character.Schema.Y != destinationMap.Y
            || character.Schema.Layer != destinationMap.Layer
        )
        {
            await NavigateNextStep(destinationMap);
        }

        return new None();
    }

    public async Task NavigateNextStep(MapSchema destinationMap)
    {
        MapSchema currentMap = gameState.MapsDict[character.Schema.MapId];

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
            Logger.LogInformation(
                $"{Name}: [{character.Name}]: Transitioning involving islands - moving from {currentMap.Name} -> {destinationMap.Name}"
            );

            if (currentMap.Layer != MapLayer.Overworld)
            {
                MapSchema? closestTransition = FindClosestTransition(currentMap);

                if (closestTransition is null)
                {
                    throw new Exception($"Cannot find transition, should not happen");
                }

                Logger.LogInformation(
                    $"{Name}: [{character.Name}]: Transitioning involving islands - not in the overworld, going up"
                );

                await character.Move(closestTransition.X, closestTransition.Y);
                await character.Transition();
                return;
            }

            if (goingFromIslandToIsland)
            {
                Logger.LogInformation(
                    $"{Name}: [{character.Name}]: Transitioning involving islands - island hopping! We need to go from {currentMap.Name} -> main land first"
                );
                // We want to go to the mainland, to go to the other island
                goingFromIslandToMainland = true;
            }

            // Going to Sandwhisper
            if (goingToIslandFromMainland)
            {
                // TODO: Should check if we have enough money etc

                var matchingTransition = transitionsToIslandFromMainland[destinationMap.Name];

                await character.Move(matchingTransition.X, matchingTransition.Y);
                await character.Transition();
                return;
                // We are going to an island
            }

            if (goingFromIslandToMainland)
            {
                var matchingTransition = transitionsFromIslandToMainland[currentMap.Name];
                // We are going back from an island
                // Boat to Sandwhisper Isle
                await character.Move(matchingTransition.X, matchingTransition.Y);
                // TODO: Consider using a recall potion if you have one
                await character.Transition();
                return;
            }
        }

        // We aren't moving between islands, and no transition should be necessary
        if (
            character.Schema.Layer == MapLayer.Overworld
            && destinationMap.Layer == MapLayer.Overworld
        )
        {
            await character.Move(destinationMap.X, destinationMap.Y);
            return;
        }

        if (character.Schema.Layer != MapLayer.Overworld)
        {
            // No matter what, we need to find out whether we are moving within the same "underground cell", or to another one

            Logger.LogInformation(
                $"{Name}: [{character.Name}]: Currently not in the overworld ({currentMap.Layer})"
            );

            MapSchema? currentClosestTransition = FindClosestTransition(currentMap)!;

            if (character.Schema.Layer == destinationMap.Layer)
            {
                MapSchema? destinationClosestTransition = FindClosestTransition(destinationMap)!;

                // We are in the same cell, the transitions are the same
                if (currentClosestTransition.MapId == destinationClosestTransition.MapId)
                {
                    Logger.LogInformation(
                        $"{Name}: [{character.Name}]: Currently not in the overworld ({currentMap.Layer}), but moving inside the same \"cell\" - no need to transition"
                    );
                    await character.Move(destinationMap.X, destinationMap.Y);
                    return;
                }
            }
            else
            {
                Logger.LogInformation(
                    $"{Name}: [{character.Name}]: Currently not in the overworld ({currentMap.Layer}) - need to transition first - moving to ({currentClosestTransition.X}, {currentClosestTransition.Y})"
                );
                // don't care if we are going from e.g. underground -> overworld or interior, we need to go to the overworld first
                await character.Move(currentClosestTransition.X, currentClosestTransition.Y);
                await character.Transition();
                return;
            }
        }

        if (
            character.Schema.Layer == MapLayer.Overworld
            && destinationMap.Layer != character.Schema.Layer
        )
        {
            /* We know that normal overworld -> other layer transitions are directly above their counterpart, e.g. if the door to a house is on position 1,1,
               then when we transition through that door, we will be on 1,1 in the interior or underground layer. So we can just calculate the distance from where
               we are, and to the destination map, and find the closest transition point from the destination map. It's not entirely bullet-proof, but it should be
               good enough.
            */
            int closestCostToTransition = 0;

            MapSchema? closestTransition = null;

            foreach (var map in gameState.Maps)
            {
                if (map.Layer != character.Schema.Layer || map.Interactions.Transition is null)
                {
                    continue;
                }

                if (map.Interactions.Transition.Layer != destinationMap.Layer)
                {
                    continue;
                }

                int cost = CalculationService.CalculateDistanceToMap(
                    character.Schema.X,
                    character.Schema.Y,
                    destinationMap.X,
                    destinationMap.Y
                );

                if (closestTransition is null || cost < closestCostToTransition)
                {
                    closestTransition = map;
                }
            }

            if (closestTransition is null)
            {
                throw new AppError(
                    $"Could not find transition to get to {destinationMap.Name} - x = {destinationMap.X} y = {destinationMap.Y}",
                    ErrorStatus.NotFound
                );
            }

            if (closestTransition.Access?.Conditions?.Count > 0)
            {
                Logger.LogDebug(
                    $"Condition to go to {destinationMap.MapId} ({destinationMap.X}, {destinationMap.Y})"
                );
            }

            Logger.LogInformation(
                $"{Name}: [{character.Name}]: Going inside from the overworld - moving to ({closestTransition.X}, {closestTransition.Y}) and transitioning"
            );

            await character.Move(closestTransition.X, closestTransition.Y);
            await character.Transition();
            return;
        }

        throw new AppError($"Should never get here");
    }

    public async Task<OneOf<AppError, None>> MoveToMap(string code)
    {
        // We don't know what it is, but it might be an item we wish to get

        var maps = gameState.Maps.FindAll(map =>
        {
            bool matchesCode = map.Interactions.Content?.Code == code;

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
            var map = gameState.EventService.WhereIsEntityActive(code);

            if (map is null)
            {
                throw new Exception($"Could not find map with code {code}");
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

            if (cost < closestCost)
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
                $"Could not find closest map to find \"{code}\"",
                ErrorStatus.NotFound
            );
        }
        if (destinationMap.Layer != character.Schema.Layer)
        {
            return new AppError($"Cannot move to other layers", ErrorStatus.NotFound);
        }

        var currentMap = gameState.MapsDict[character.Schema.MapId];

        // Going to Sandwhisper
        if (
            Islands.Contains(destinationMap.Name) && !Islands.Contains(currentMap.Name)
            || !Islands.Contains(destinationMap.Name) && Islands.Contains(currentMap.Name)
        )
        {
            return new AppError($"Cannot move between islands", ErrorStatus.NotFound);
        }

        await character.Move(destinationMap.X, destinationMap.Y);

        return new None();
    }

    public MapSchema? FindClosestTransition(MapSchema currentMap)
    {
        MapSchema? closestTransition = null;
        int closestCostToTransition = 0;

        foreach (var map in gameState.Maps)
        {
            if (map.Layer != currentMap.Layer || map.Interactions.Transition is null)
            {
                continue;
            }

            if (map.Interactions.Transition.Layer != map.Layer)
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
}
