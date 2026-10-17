using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Bounded derived node history from native hourly rollups; it grants no canonical read cut or RF3 receipt.</summary>
/// <param name="From">Inclusive UTC hour boundary.</param>
/// <param name="To">Exclusive UTC hour boundary.</param>
/// <param name="NativePlanComplete">Native planning coverage, never complete sampled history or zero loss.</param>
/// <param name="Rows">Fixed numeric hourly rows without dimensions or caller data.</param>
/// <param name="Counters">Process-local native ingestion and loss counters.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(OperationalAnalyticsContractAliases.Report)]
public sealed record OperationalAnalyticsReport([property: Orleans.Id(0)] DateTimeOffset From,
    [property: Orleans.Id(1)] DateTimeOffset To, [property: Orleans.Id(2)] bool NativePlanComplete,
    [property: Orleans.Id(3)] ImmutableArray<OperationalAnalyticsRow> Rows,
    [property: Orleans.Id(4)] OperationalAnalyticsCounters Counters);
