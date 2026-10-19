using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Returns leased deliveries and their committed receive token.</summary>
/// <param name="RequestId">Identifies this receive request.</param>
/// <param name="Deliveries">Contains leased messages.</param>
/// <param name="Token">Carries the delivery token.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.ReceiveResult)]
public sealed record ReceiveResult([property: Orleans.Id(0)] Guid RequestId, [property: Orleans.Id(1)] ImmutableArray<Delivery> Deliveries, [property: Orleans.Id(2)] CommitToken Token);
