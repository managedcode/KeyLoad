namespace KeyLoad.Core.TimeSeries;

internal static class SampleChunkLifecycleProtocol
{
    internal const int Version = 1;
    internal const long Absent = 0;
    internal const long First = 1;
    internal const int FirstIndex = 0;
    internal const string WindowAlias = "keyload.core.sample-chunk-window";
    internal const string ManifestAlias = "keyload.core.sample-chunk-manifest";
    internal const string WindowSpace = "sample-chunk-window";
    internal const string BlockSpace = "sample-chunk-block";
    internal const string CorrectionSpace = "sample-chunk-correction";
    internal const string ManifestSpace = "sample-chunk-manifest";
    internal const string Corrupt = "The execution series chunk representation is inconsistent.";
    internal const string Unsupported = "The execution series chunk representation version is unsupported.";
    internal const string Invalid = "The execution series chunk window request is invalid.";
    internal const string Exhausted = "The execution series chunk window exceeds its admitted bound.";
    internal const string RevisionConflict = "The execution series chunk window revision changed.";
    internal const string Existing = "The execution series chunk window overlaps retained data or an enrolled identity.";
    internal const string Missing = "The execution series chunk window is unavailable.";
    internal const string OpenKind = "openSampleChunkWindow";
    internal const string SealKind = "sealSampleChunkWindow";
    internal const string MergeKind = "mergeSampleChunkWindow";
    internal const string DropKind = "dropSampleChunkWindow";
}
