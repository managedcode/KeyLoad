using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>One canonical event revision input; native analytics indexes are derived from committed events.</summary>
/// <param name="Id">Stable event identity within the store and atomic partition.</param>
/// <param name="Timestamp">UTC event time.</param>
/// <param name="Dimensions">Schema-declared bounded dimension values.</param>
/// <param name="Metrics">Schema-declared finite numeric metric values.</param>
/// <param name="RowAccess">Optional persisted row ownership metadata.</param>
/// <param name="Links">Canonical entity references in the same atomic partition.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(AnalyticsStoreContractAliases.EventData)]
public sealed record AnalyticsEventData(
    [property: Orleans.Id(0)] string Id,
    [property: Orleans.Id(1)] DateTimeOffset Timestamp,
    [property: Orleans.Id(2)] ImmutableDictionary<string, string> Dimensions,
    [property: Orleans.Id(3)] ImmutableDictionary<string, double> Metrics,
    [property: Orleans.Id(4)] RowAccess? RowAccess,
    [property: Orleans.Id(5)] ImmutableArray<EntityRef> Links);
