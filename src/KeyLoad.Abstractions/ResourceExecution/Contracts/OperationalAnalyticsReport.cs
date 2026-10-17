using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>One physical node's hourly sums of sampled replica positions, independent of database acknowledgement.</summary>
/// <param name="Hour">UTC hour containing the actual samples.</param>
/// <param name="Samples">Number of accepted samples in this hourly rollup.</param>
/// <param name="CommittedSum">Sum of sampled committed positions, never a transaction count.</param>
/// <param name="AppliedSum">Sum of sampled materialized positions.</param>
/// <param name="BacklogSum">Sum of committed minus materialized positions at the actual sample cuts.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(OperationalAnalyticsContractAliases.Row)]
public sealed record OperationalAnalyticsRow([property: Orleans.Id(0)] DateTimeOffset Hour,
    [property: Orleans.Id(1)] double Samples, [property: Orleans.Id(2)] double CommittedSum,
    [property: Orleans.Id(3)] double AppliedSum, [property: Orleans.Id(4)] double BacklogSum);

/// <summary>Native analytics engine counters for this process; persisted rollups may contain older samples.</summary>
/// <param name="Accepted">Accepted native events since this engine opened.</param>
/// <param name="Processed">Processed native events since this engine opened.</param>
/// <param name="Dropped">Native queue-full losses.</param>
/// <param name="Rejected">Native rejected events.</param>
/// <param name="Errors">Native recorded errors.</param>
/// <param name="QueueLength">Native pending queue length at observation.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(OperationalAnalyticsContractAliases.Counters)]
public sealed record OperationalAnalyticsCounters([property: Orleans.Id(0)] long Accepted,
    [property: Orleans.Id(1)] long Processed, [property: Orleans.Id(2)] long Dropped,
    [property: Orleans.Id(3)] long Rejected, [property: Orleans.Id(4)] long Errors,
    [property: Orleans.Id(5)] long QueueLength);

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
