using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Contains deliveries and subscription status returned by a receive operation.</summary>
/// <param name="RequestId">The idempotent request identifier.</param>
/// <param name="Deliveries">The leased deliveries returned.</param>
/// <param name="Status">The subscription status observed for the request.</param>
/// <param name="Token">The commit token for the receive operation.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.ReceiveSubscriptionResult)]
public sealed record ReceiveSubscriptionResult([property: Orleans.Id(0)] Guid RequestId, [property: Orleans.Id(1)] ImmutableArray<SubscriptionDelivery> Deliveries, [property: Orleans.Id(2)] SubscriptionInfo Status, [property: Orleans.Id(3)] CommitToken Token);
