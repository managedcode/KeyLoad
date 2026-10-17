namespace KeyLoad;

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
