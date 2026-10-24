using System.Collections.Immutable;
using KeyLoad.Clustering;

namespace KeyLoad.UnitTests.ClientApi;

/// <summary>Independent frozen time-series operations in the public MCP contract inventory.</summary>
internal static class McpSeriesCatalogExpectations
{
    private const string ChunkWindowTool = "keyload_series_chunk_window";
    private const string ChunkWindowRoute = "/series/chunks/window";
    private const string SeriesReadRoute = "/series/read";
    private const string SeriesLatestRoute = "/series/latest";
    private const string SeriesAggregateRoute = "/series/aggregate";
    private const string SeriesWindowsRoute = "/series/windows";
    private const string SeriesRollupRoute = "/series/rollups/read";
    private const string SeriesRetentionRoute = "/series/retention";

    internal static ImmutableArray<(string Name, string Route, GrainReadKind? ReadKind, OperationKind? CommandKind)> Entries
    {
        get;
    } =
    [
        (McpCatalogExpectations.SeriesRead, SeriesReadRoute, GrainReadKind.Samples, null),
        (McpCatalogExpectations.SeriesLatest, SeriesLatestRoute, GrainReadKind.LatestSample, null),
        (McpCatalogExpectations.SeriesAggregate, SeriesAggregateRoute, GrainReadKind.AggregateSamples, null),
        (McpCatalogExpectations.SeriesWindows, SeriesWindowsRoute, GrainReadKind.AggregateSampleWindows, null),
        (McpCatalogExpectations.SeriesRollup, SeriesRollupRoute, GrainReadKind.SampleRollup, null),
        (McpCatalogExpectations.SeriesRetention, SeriesRetentionRoute, GrainReadKind.SampleRetention, null),
        (ChunkWindowTool, ChunkWindowRoute, GrainReadKind.SampleChunkWindow, null),
    ];
}
