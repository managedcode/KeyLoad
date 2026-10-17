namespace KeyLoad;

/// <summary>Exact native enrolled chunk-window operation identities.</summary>
public static class SampleChunkProtocol
{
    /// <summary>Opens an initially empty native window.</summary>
    public const string OpenKind = "openSampleChunkWindow";
    /// <summary>Seals an admitted open window.</summary>
    public const string SealKind = "sealSampleChunkWindow";
    /// <summary>Merges original correction records into one new immutable generation.</summary>
    public const string MergeKind = "mergeSampleChunkWindow";
    /// <summary>Retires an entirely expired window representation.</summary>
    public const string DropKind = "dropSampleChunkWindow";
    /// <summary>Bounded enrolled-window read route.</summary>
    public const string ReadRoute = "/v1/series/chunks/window";
    /// <summary>Official tool identity for the same authorized native read.</summary>
    public const string ReadTool = "keyload_series_chunk_window";
}
