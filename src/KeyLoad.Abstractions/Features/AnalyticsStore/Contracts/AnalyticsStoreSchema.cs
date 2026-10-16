using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Declares the closed dimension and finite numeric metric names of one independent analytics store.</summary>
/// <param name="Dimensions">Ordinal, unique dimension names.</param>
/// <param name="Metrics">Ordinal, unique metric names.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(AnalyticsStoreContractAliases.Schema)]
public sealed record AnalyticsStoreSchema(
    [property: Orleans.Id(0)] ImmutableArray<string> Dimensions,
    [property: Orleans.Id(1)] ImmutableArray<string> Metrics);
