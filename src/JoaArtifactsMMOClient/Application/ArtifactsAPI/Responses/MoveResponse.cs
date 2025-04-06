using System.Text.Json.Serialization;
using Applcation.ArtifactsAPI.Dtos;
using Microsoft.VisualBasic;

public record MoveResponse
{
    public required MoveResponseData data;
}

public record MoveResponseData
{
    Cooldown cooldown;
    DestinationDto destination;
    CharacterDto character;
}
