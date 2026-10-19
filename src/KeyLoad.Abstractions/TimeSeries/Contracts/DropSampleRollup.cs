namespace KeyLoad;

/// <summary>Drops derived statistics while retaining the monotone revision tombstone.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(SampleRollupAliases.DropSampleRollup)]
public sealed record DropSampleRollup(
    [property: Orleans.Id(0)] string SeriesSet, [property: Orleans.Id(1)] string SeriesId,
    [property: Orleans.Id(2)] DateTimeOffset From, [property: Orleans.Id(3)] DateTimeOffset UntilExclusive,
    [property: Orleans.Id(4)] long ExpectedRevision) : Mutation(SeriesSet);
