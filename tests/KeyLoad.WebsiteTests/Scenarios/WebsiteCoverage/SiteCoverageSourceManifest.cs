namespace KeyLoad.WebsiteTests.BenchmarkComparisons;

internal sealed record SiteCoverageSourceManifest(int SchemaVersion, string SourceRevision, string NodeVersion,
    IReadOnlyList<SiteCoverageSourceEntry> Sources);
