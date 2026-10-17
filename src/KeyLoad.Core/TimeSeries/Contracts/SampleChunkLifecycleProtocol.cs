namespace KeyLoad.Core.Features.TimeSeries;

internal static class SampleChunkLifecycleProtocol
{
    internal const int Version = 1;
    internal const long Absent = 0;
    internal const long First = 1;
    internal const int FirstIndex = 0;
    internal const string WindowAlias = "keyload.core.sample-chunk-window.v1";
    internal const string ManifestAlias = "keyload.core.sample-chunk-manifest.v1";
    internal const string WindowSpace = "sample-chunk-window";
    internal const string BlockSpace = "sample-chunk-block";
    internal const string CorrectionSpace = "sample-chunk-correction";
    internal const string ManifestSpace = "sample-chunk-manifest";
    internal const string Corrupt = "The native series chunk representation is inconsistent.";
    internal const string Unsupported = "The native series chunk representation version is unsupported.";
    internal const string Invalid = "The native series chunk window request is invalid.";
    internal const string Exhausted = "The native series chunk window exceeds its admitted bound.";
    internal const string RevisionConflict = "The native series chunk window revision changed.";
    internal const string Existing = "The native series chunk window overlaps retained data or an enrolled identity.";
    internal const string Missing = "The native series chunk window is unavailable.";
    internal const string OpenKind = "openSampleChunkWindow";
    internal const string SealKind = "sealSampleChunkWindow";
    internal const string MergeKind = "mergeSampleChunkWindow";
    internal const string DropKind = "dropSampleChunkWindow";
}
