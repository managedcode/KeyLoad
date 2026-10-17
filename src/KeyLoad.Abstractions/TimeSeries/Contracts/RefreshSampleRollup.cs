namespace KeyLoad;

/// <summary>Rebuilds one complete explicit half-open UTC bucket from canonical raw samples.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(SampleRollupNativeAliases.RefreshSampleRollup)]
public sealed record RefreshSampleRollup(
    [property: Orleans.Id(0)] string SeriesSet, [property: Orleans.Id(1)] string SeriesId,
    [property: Orleans.Id(2)] DateTimeOffset From, [property: Orleans.Id(3)] DateTimeOffset UntilExclusive,
    [property: Orleans.Id(4)] long ExpectedRevision,
    [property: Orleans.Id(5)] int MaxSamples = TimeSeriesReadDefaults.MaxSamples) : Mutation(SeriesSet);
