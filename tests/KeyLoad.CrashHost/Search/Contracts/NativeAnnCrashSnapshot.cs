namespace KeyLoad.CrashHost.Features.Search;

internal sealed record NativeAnnCrashSnapshot(VectorRecord[] Records, AnnSourceCut Source,
    string IndexSha256, string Receipt, string CanonicalImage, long Position);
