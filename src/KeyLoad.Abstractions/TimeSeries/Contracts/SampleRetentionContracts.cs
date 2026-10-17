namespace KeyLoad;

/// <summary>Defines bounded native time-series retention pages.</summary>
public static class SampleRetentionDefaults
{
    /// <summary>Gets the default number of expired records removed by one command.</summary>
    public const int DefaultDeletes = 128;
    /// <summary>Gets the maximum number of expired records removed by one command.</summary>
    public const int MaximumDeletes = 256;
}

/// <summary>Advances an exclusive UTC retention floor and removes a bounded expired page.</summary>
/// <param name="SeriesSet">The configured time-series resource.</param>
/// <param name="SeriesId">The canonical series identity.</param>
/// <param name="Before">Samples strictly before this trusted cutoff become unavailable.</param>
/// <param name="MaximumDeletes">Maximum expired records removed in this atomic command.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.ExpireSamples)]
public sealed record ExpireSamples([property: Orleans.Id(0)] string SeriesSet,
    [property: Orleans.Id(1)] string SeriesId, [property: Orleans.Id(2)] DateTimeOffset Before,
    [property: Orleans.Id(3)] int MaximumDeletes = SampleRetentionDefaults.DefaultDeletes) : Mutation(SeriesSet);

/// <summary>Requests the persisted retention progress of one series.</summary>
/// <param name="Partition">The owning atomic partition.</param>
/// <param name="Set">The configured time-series resource.</param>
/// <param name="SeriesId">The canonical series identity.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.ReadSampleRetentionRequest)]
public sealed record ReadSampleRetentionRequest([property: Orleans.Id(0)] PartitionRef Partition,
    [property: Orleans.Id(1)] string Set, [property: Orleans.Id(2)] string SeriesId);

/// <summary>Reports an exclusive retention floor and its bounded physical deletion progress.</summary>
/// <param name="Before">The exclusive UTC floor, or null before retention is configured.</param>
/// <param name="PurgedCount">Cumulative expired records removed by committed pages.</param>
/// <param name="HasMore">Whether the latest page observed more expired records.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.SampleRetentionStatus)]
public sealed record SampleRetentionStatus([property: Orleans.Id(0)] DateTimeOffset? Before,
    [property: Orleans.Id(1)] long PurgedCount, [property: Orleans.Id(2)] bool HasMore);
