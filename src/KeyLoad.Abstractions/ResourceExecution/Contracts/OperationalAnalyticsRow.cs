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
