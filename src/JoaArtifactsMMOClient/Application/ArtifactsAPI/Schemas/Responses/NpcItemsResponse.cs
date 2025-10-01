using System.Text.Json.Serialization;

namespace Application.ArtifactsApi.Schemas.Responses;

public record NpcItemsResponse : PaginatedResult
{
    public required List<NpcItemSchema> Data { get; set; } = [];
}

public record NpcItemSchema
{
    public string Code { get; set; } = "";

    public string Npc { get; set; } = "";

    public string Currency { get; set; } = "";

    public int? BuyPrice { get; set; } = null;

    public int? SellPrice { get; set; } = null;
}
