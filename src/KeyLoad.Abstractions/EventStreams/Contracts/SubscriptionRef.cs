namespace KeyLoad;

/// <summary>Identifies one subscription group on an event source.</summary>
/// <param name="Source">The subscribed event source.</param>
/// <param name="GroupId">The subscription group identifier.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.SubscriptionRef)]
public sealed record SubscriptionRef([property: Orleans.Id(0)] EventSourceRef Source, [property: Orleans.Id(1)] string GroupId);
