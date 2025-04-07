using Applcation.ArtifactsAPI.Dtos;
using Application.Character;

public class GameState
{
    List<PlayerCharacter> characters { get; set; }
    List<Item> items { get; set; }

    List<MapDto> maps { get; set; }

    List<Resource> resources { get; set; }

    public GameState() { }
}
