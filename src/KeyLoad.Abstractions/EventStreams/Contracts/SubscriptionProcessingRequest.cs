using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Requests processing a delivered event and applying its resulting mutations.</summary>
/// <param name="CommandId">The idempotent command identifier.</param>
/// <param name="Subscription">The subscription owning the delivery.</param>
/// <param name="Token">The delivery acknowledgement token.</param>
/// <param name="HandlerScope">The stable scope identifying the event handler.</param>
/// <param name="ExecutionGeneration">The handler execution generation.</param>
/// <param name="Effects">The mutations produced by processing the event.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.SubscriptionProcessingRequest)]
public sealed record SubscriptionProcessingRequest([property: Orleans.Id(0)] Guid CommandId, [property: Orleans.Id(1)] SubscriptionRef Subscription, [property: Orleans.Id(2)] string Token, [property: Orleans.Id(3)] string HandlerScope,
    [property: Orleans.Id(4)] long ExecutionGeneration, [property: Orleans.Id(5)] ImmutableArray<Mutation> Effects);
