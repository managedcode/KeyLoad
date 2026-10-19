namespace KeyLoad;

/// <summary>Reports the current checkpoint, delivery position, and state of a subscription.</summary>
/// <param name="Subscription">The subscription identity.</param>
/// <param name="Definition">The active subscription definition.</param>
/// <param name="Generation">The subscription configuration generation.</param>
/// <param name="OwnershipEpoch">The current ownership epoch.</param>
/// <param name="Checkpoint">The highest acknowledged source position.</param>
/// <param name="IssuedPosition">The highest position issued for delivery.</param>
/// <param name="TailPosition">The latest source position observed.</param>
/// <param name="Paused">Whether delivery is paused.</param>
/// <param name="SafeFailureCode">A safe failure code when processing has failed.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.SubscriptionInfo)]
public sealed record SubscriptionInfo([property: Orleans.Id(0)] SubscriptionRef Subscription, [property: Orleans.Id(1)] SubscriptionDefinition Definition, [property: Orleans.Id(2)] long Generation,
    [property: Orleans.Id(3)] long OwnershipEpoch, [property: Orleans.Id(4)] long Checkpoint, [property: Orleans.Id(5)] long IssuedPosition, [property: Orleans.Id(6)] long TailPosition, [property: Orleans.Id(7)] bool Paused, [property: Orleans.Id(8)] string? SafeFailureCode);
