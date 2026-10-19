namespace KeyLoad;

/// <summary>Reports an exclusive retention floor and its bounded physical deletion progress.</summary>
/// <param name="Before">The exclusive UTC floor, or null before retention is configured.</param>
/// <param name="PurgedCount">Cumulative expired records removed by committed pages.</param>
/// <param name="HasMore">Whether the latest page observed more expired records.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.SampleRetentionStatus)]
public sealed record SampleRetentionStatus([property: Orleans.Id(0)] DateTimeOffset? Before,
    [property: Orleans.Id(1)] long PurgedCount, [property: Orleans.Id(2)] bool HasMore);
