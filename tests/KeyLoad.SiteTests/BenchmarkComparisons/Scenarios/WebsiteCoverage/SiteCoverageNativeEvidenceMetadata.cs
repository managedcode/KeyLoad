namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed record SiteCoverageNativeEvidenceMetadata(string FixtureSourcePath, string FixtureSourceSha256,
    string ReceiptIdentity, string ReceiptSha256, string SourceFile, string NativeReceiptFile, string StandardOutputFile);
