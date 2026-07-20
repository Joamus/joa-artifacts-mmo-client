using Application.ArtifactsApi.Schemas;
using Application.Errors;
using OneOf;

namespace Application.Services;

public class PathfindingService
{
    public Dictionary<int, Zone> MapIdToZoneMap { get; set; }

    // Not sure about the map size
    public List<Zone> Zones { get; set; }

    CoordList CoordList { get; set; }

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
    static PathfindingService _pathFindingService { get; set; }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

    public static PathfindingService GetInstance(List<MapSchema> maps)
    {
        if (_pathFindingService is null)
        {
            _pathFindingService = new PathfindingService(maps);
        }

        return _pathFindingService;
    }

    PathfindingService(List<MapSchema> maps)
    {
        CoordList coordList = new(maps);

        Zones = GetAllZones(maps, coordList);

        MapIdToZoneMap = [];

        foreach (var zone in Zones)
        {
            foreach (var map in zone.Maps)
            {
                MapIdToZoneMap.Add(map.MapId, zone);
            }
        }

        CoordList = coordList;
    }

    static List<Zone> GetAllZones(List<MapSchema> maps, CoordList coordList)
    {
        var spawn = maps.Find(map => map.X == 0 && map.Y == 0 && map.Layer == MapLayer.Overworld)!;

        Zone firstZone = new()
        {
            Id = Guid.NewGuid(),
            Maps = [],
            TransitionMaps = [],
        };

        var zones = InnerGetAllZones(firstZone, spawn, new CoordList(maps), [], []);

        return zones;
    }

    static bool IsMapAccessible(MapSchema map)
    {
        return map.Access.Type != AccessType.Blocked;
    }

    static List<MapSchema> GetNeighbours(
        MapSchema currentMap,
        CoordList coordList,
        HashSet<int> visitedMapIds,
        Dictionary<int, Zone> mapIdToZoneMap
    )
    {
        List<MapSchema> neighbours = [];

        List<(int X, int Y)> neighbourCoordinates =
        [
            (currentMap.X - 1, currentMap.Y),
            (currentMap.X + 1, currentMap.Y),
            (currentMap.X, currentMap.Y - 1),
            (currentMap.X, currentMap.Y + 1),
        ];

        neighbours =
        [
            .. neighbourCoordinates
                .Select(coords =>
                {
                    var matchingMap = coordList.GetMap(coords.X, coords.Y, currentMap.Layer);

                    return matchingMap;
                })
                .OfType<MapSchema>()
                .Where(map =>
                    IsMapAccessible(map)
                    && !visitedMapIds.Contains(map.MapId)
                    && !mapIdToZoneMap.ContainsKey(map.MapId)
                ),
        ];

        return neighbours;
    }

    static void PopulateZone(
        Zone zone,
        CoordList coordList,
        MapSchema currentMap,
        HashSet<int> visitedMapIds,
        Dictionary<int, Zone> mapIdToZoneMap
    )
    {
        if (visitedMapIds.Contains(currentMap.MapId))
        {
            return;
        }

        zone.Maps.Add(currentMap);

        if (currentMap.Interactions.Transition is not null)
        {
            zone.TransitionMaps.Add(currentMap);
        }

        visitedMapIds.Add(currentMap.MapId);
        mapIdToZoneMap.Add(currentMap.MapId, zone);

        var neighbours = GetNeighbours(currentMap, coordList, visitedMapIds, mapIdToZoneMap);

        foreach (var neighbour in neighbours)
        {
            PopulateZone(zone, coordList, neighbour, visitedMapIds, mapIdToZoneMap);
        }
    }

    static List<Zone> InnerGetAllZones(
        Zone initialZone,
        MapSchema initialMap,
        CoordList coordList,
        HashSet<int> visitedMapIds,
        Dictionary<int, Zone> mapIdToZoneMap
    )
    {
        List<Zone> zones = [];

        PopulateZone(initialZone, coordList, initialMap, visitedMapIds, mapIdToZoneMap);

        PopulateAllTransitions(zones, initialZone, coordList, visitedMapIds, mapIdToZoneMap);

        zones.Add(initialZone);

        return zones;
    }

    static void PopulateAllTransitions(
        List<Zone> zones,
        Zone initialZone,
        CoordList coordList,
        HashSet<int> visitedMapIds,
        Dictionary<int, Zone> mapIdToZoneMap
    )
    {
        foreach (var transitionMap in initialZone.TransitionMaps)
        {
            var transition = transitionMap.Interactions.Transition!;

            var destination = coordList.GetMap(transition.X, transition.Y, transition.Layer)!;

            if (visitedMapIds.Contains(destination.MapId))
            {
                continue;
            }

            Zone zoneForTransition = new()
            {
                Id = Guid.NewGuid(),
                Maps = [],
                TransitionMaps = [],
            };

            PopulateZone(zoneForTransition, coordList, destination, visitedMapIds, mapIdToZoneMap);
            PopulateAllTransitions(
                zones,
                zoneForTransition,
                coordList,
                visitedMapIds,
                mapIdToZoneMap
            );
            zones.Add(zoneForTransition);
        }
    }

    public OneOf<AppError, List<Zone>> GetZonesToDestination(Zone currentZone, Zone destinationZone)
    {
        if (currentZone.Id == destinationZone.Id)
        {
            return (List<Zone>)[currentZone];
        }

        HashSet<Guid> visitedZoneIds = [];
        Queue<List<Zone>> queue = [];

        queue.Enqueue([currentZone]);

        while (queue.Count > 0)
        {
            var path = queue.Dequeue();

            foreach (Zone zone in path)
            {
                if (visitedZoneIds.Contains(zone.Id))
                {
                    continue;
                }

                visitedZoneIds.Add(zone.Id);

                foreach (var transition in zone.TransitionMaps)
                {
                    Zone transitionDestinationZone = MapIdToZoneMap[
                        transition.Interactions.Transition!.MapId
                    ];

                    List<Zone> pathForZone = [.. path, transitionDestinationZone];

                    if (transitionDestinationZone.Id == destinationZone.Id)
                    {
                        return pathForZone;
                    }

                    queue.Enqueue(pathForZone);
                }
            }
        }

        return new AppError("Could not find transitions to destination zone");
    }

    void InnerGetTransitionsToDestination(
        List<MapSchema> transitions,
        MapSchema currentMap,
        MapSchema destinationMap,
        HashSet<Guid> visitedZoneIds,
        Dictionary<int, Zone> mapIdToZoneMap
    )
    {
        // Figure out what kind of object that should be passed here and updated for each hop
        Zone currentMapZone = mapIdToZoneMap[currentMap.MapId];
        Zone? destinationMapZone = mapIdToZoneMap.GetValueOrNull(destinationMap.MapId);

        if (destinationMapZone is not null && currentMapZone.Id == destinationMapZone.Value.Id)
        {
            return;
        }

        foreach (var transitionMap in currentMapZone.TransitionMaps)
        {
            var transition = transitionMap.Interactions.Transition!;

            // Add this hop above to the state
            var transitionDestination = CoordList.GetMap(
                transition.X,
                transition.Y,
                transition.Layer
            )!;

            Zone? transitionDestinationZone = mapIdToZoneMap.GetValueOrNull(
                transitionDestination.MapId
            );

            if (
                transitionDestinationZone is null
                || !visitedZoneIds.Contains(transitionDestinationZone.Value.Id)
            )
            {
                InnerGetTransitionsToDestination(
                    transitions,
                    transitionDestination,
                    destinationMap,
                    visitedZoneIds,
                    mapIdToZoneMap
                );
            }
        }
    }
}

public class CoordList
{
    public Dictionary<MapLayer, Dictionary<int, Dictionary<int, MapSchema>>> Data
    {
        private get;
        init;
    } = [];

    public CoordList(List<MapSchema> maps)
    {
        Data = GetMapsAsLayeredCoordList(maps);
    }

    public MapSchema? GetMap(MapSchema map)
    {
        return GetMap(map.X, map.Y, map.Layer);
    }

    public MapSchema? GetMap(int x, int y, MapLayer mapLayer)
    {
        var layerMaps = Data.GetValueOrNull(mapLayer);

        if (layerMaps is null)
        {
            return null;
        }

        var mapsWithXCoordinate = layerMaps.GetValueOrNull(x);

        if (mapsWithXCoordinate is null)
        {
            return null;
        }

        var map = mapsWithXCoordinate.GetValueOrNull(y);

        return map;
    }

    // public void SetMapIsVisited(PathfindingMap map)
    // {
    //     map.IsVisited = true;
    // }

    static Dictionary<
        MapLayer,
        Dictionary<int, Dictionary<int, MapSchema>>
    > GetMapsAsLayeredCoordList(List<MapSchema> maps)
    {
        Dictionary<MapLayer, Dictionary<int, Dictionary<int, MapSchema>>> coordList = [];

        foreach (var map in maps)
        {
            if (coordList.GetValueOrNull(map.Layer) is null)
            {
                coordList.Add(map.Layer, []);
            }

            var mapsWithXPosition = coordList[map.Layer];

            if (mapsWithXPosition.GetValueOrNull(map.X) is null)
            {
                mapsWithXPosition.Add(map.X, []);
            }

            var mapsWithYPosition = coordList[map.Layer];

            if (mapsWithYPosition.GetValueOrNull(map.Y) is null)
            {
                mapsWithYPosition.Add(map.Y, []);
            }

            // mapsWithXPosition[map.X][map.Y] = new PathfindingMap { Map = map, IsVisited = false };
            mapsWithXPosition[map.X][map.Y] = map;
        }

        return coordList;
    }
}

public struct Zone
{
    public required Guid Id { get; init; }

    public required List<MapSchema> Maps { get; set; }

    public required List<MapSchema> TransitionMaps { get; set; }
}

// public class PathfindingMap
// {
//     // public required bool IsVisited { get; set; }
//     public required MapSchema Map { get; init; }
//     public Guid? ZoneId { get; set; }
// }
