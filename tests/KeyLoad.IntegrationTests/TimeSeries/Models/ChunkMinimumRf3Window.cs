using System.Collections.Immutable;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal sealed record ChunkMinimumRf3Window(Guid Id, string Series, long Revision, long Sequence,
    ImmutableArray<SampleRecord> Rows, CommitToken Minimum)
{
    internal ReadSampleChunkWindowRequest Request(PartitionRef partition)
        => new(partition, TimeSeriesRf3Scenario.Set, Series, Id, null, null, Rows.Length);
}
