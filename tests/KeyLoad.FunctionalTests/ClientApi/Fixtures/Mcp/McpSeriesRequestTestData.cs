namespace KeyLoad.FunctionalTests.ClientApi;

/// <summary>Complete current time-series request values for the existing canonical MCP decoder corpus.</summary>
internal static class McpSeriesRequestTestData
{
    private const int RangeMinutes = 2;
    private const int MaximumPoints = 11;
    private const int MaximumWindows = 2;
    private const int Limit = 10;
    private const long MinimumPosition = 17;
    private const string TagPointer = "/kind";
    private const string TagValue = "metric";
    private static readonly DateTimeOffset Until = DateTimeOffset.UnixEpoch.AddMinutes(RangeMinutes);
    [ImmutableTemporalData]
    private static readonly TimeSpan Width = TimeSpan.FromMinutes(1);

    internal static ReadLatestSampleRequest Latest() => new(McpCanonicalTestData.Partition,
        McpCanonicalTestData.Resource, McpCanonicalTestData.Entity, Until);

    internal static AggregateSamplesRequest Aggregate() => new(McpCanonicalTestData.Partition,
        McpCanonicalTestData.Resource, McpCanonicalTestData.Entity, DateTimeOffset.UnixEpoch, Until, MaximumPoints);

    internal static AggregateSampleWindowsRequest Windows() => new(McpCanonicalTestData.Partition,
        McpCanonicalTestData.Resource, McpCanonicalTestData.Entity, DateTimeOffset.UnixEpoch, Until, Width,
        MaximumPoints, MaximumWindows);

    internal static ReadSampleChunkWindowRequest Chunk() => new(McpCanonicalTestData.Partition,
        McpCanonicalTestData.Resource, McpCanonicalTestData.Entity, McpCanonicalTestData.StableId,
        TagPointer, TagValue, Limit)
    {
        MinimumToken = new(McpCanonicalTestData.StableId, McpCanonicalTestData.Partition.AtomicPartitionId, MinimumPosition)
    };
}
