namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed record SiteCoverageSourceManifest(int SchemaVersion, string SourceRevision, string NodeVersion,
    IReadOnlyList<SiteCoverageSourceEntry> Sources);
