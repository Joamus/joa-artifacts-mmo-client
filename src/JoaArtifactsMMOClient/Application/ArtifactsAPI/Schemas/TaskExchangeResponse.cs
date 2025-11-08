namespace Application.ArtifactsApi.Schemas;

// Response that contains cooldown and
//
public record TasksExchangeResponse
{
    public required RewardDataSchema Data { get; set; }
}

public record RewardDataSchema : GenericCharacterSchema
{
    public required RewardsSchema Rewards { get; set; }
}

public record RewardsSchema
{
    public required List<SimpleItemSchema> Items = [];

    public required int Gold;
}
