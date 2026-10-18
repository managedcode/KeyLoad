namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed record SiteIsolatedExpectedRow(string Name, string Status, double? Value, int NodeCount,
    string WorkerId, long JobId, long ArtifactId);
