namespace KeyLoad.SiteTests.BenchmarkComparisons;

internal sealed record SiteCoverageEvidenceMetadata(string FixtureSourcePath, string FixtureSourceSha256,
    string ReceiptIdentity, string ReceiptSha256, string SourceFile, string RuntimeReceiptFile, string StandardOutputFile);
