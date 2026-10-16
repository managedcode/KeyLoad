using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>One authorized canonical analytics event and its committed revision.</summary>
/// <param name="Id">Stable event identity.</param>
/// <param name="Timestamp">UTC event time.</param>
/// <param name="Dimensions">Authorized schema-declared dimensions.</param>
/// <param name="Metrics">Authorized schema-declared metrics.</param>
/// <param name="RowAccess">Persisted row access metadata.</param>
/// <param name="Links">Canonical entity references.</param>
/// <param name="Revision">Monotone canonical event revision.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(AnalyticsStoreContractAliases.EventRecord)]
public sealed record AnalyticsEventRecord(
    [property: Orleans.Id(0)] string Id,
    [property: Orleans.Id(1)] DateTimeOffset Timestamp,
    [property: Orleans.Id(2)] ImmutableDictionary<string, string> Dimensions,
    [property: Orleans.Id(3)] ImmutableDictionary<string, double> Metrics,
    [property: Orleans.Id(4)] RowAccess? RowAccess,
    [property: Orleans.Id(5)] ImmutableArray<EntityRef> Links,
    [property: Orleans.Id(6)] long Revision);
