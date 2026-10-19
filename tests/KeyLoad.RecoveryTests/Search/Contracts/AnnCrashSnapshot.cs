namespace KeyLoad.RecoveryTests.Features.Search;

internal sealed record AnnCrashSnapshot(VectorRecord[] Records, AnnSourceCut Source,
    string IndexSha256, string Receipt, string CanonicalImage, long Position);
