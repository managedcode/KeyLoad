namespace KeyLoad.TimeSeries;

/// <summary>Names canonical time-series data families shared by the model and physical storage router.</summary>
internal static class TimeSeriesStorageFamilies
{
    internal const string Sample = "sample";
    internal const string Identity = "sample-id";
    internal const string Retention = "sample-retention";
    internal const string Sequence = "sample-sequence";
    internal const string Rollup = "sample-rollup";
    internal const string ChunkBlock = "sample-chunk-block";
    internal const string ChunkCorrection = "sample-chunk-correction";
    internal const string ChunkManifest = "sample-chunk-manifest";
    internal const string ChunkWindow = "sample-chunk-window";

    internal static string[] All { get; } =
    [Sample, Identity, Retention, Sequence, Rollup, ChunkBlock, ChunkCorrection, ChunkManifest, ChunkWindow];
}
