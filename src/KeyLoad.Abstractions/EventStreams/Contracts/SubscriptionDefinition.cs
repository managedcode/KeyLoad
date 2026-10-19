using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Defines the principal and event-type filter for a subscription.</summary>
/// <param name="DataPrincipalId">The data principal whose scope owns the subscription.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.SubscriptionDefinition)]
public sealed record SubscriptionDefinition([property: Orleans.Id(0)] string DataPrincipalId)
{
    /// <summary>Gets or initializes the subscription retry and lease policy.</summary>
    [Orleans.Id(1)]
    public SubscriptionPolicy Policy { get; init; } = new();
    /// <summary>Gets or initializes the event type names accepted by the subscription.</summary>
    [Orleans.Id(2)]
    public ImmutableArray<string> EventTypes { get; init; } = [];
}
