using System.Net.Mime;
using System.Text.Json.Serialization;

public record DestinationDto
{
    [JsonPropertyName("name")]
    string Name = "";

    [JsonPropertyName("skin")]
    string Skin = "";

    [JsonPropertyName("x")]
    int X;

    [JsonPropertyName("y")]
    int Y;

    [JsonPropertyName("content")]
    public required ContentDto content;
}
