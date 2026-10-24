namespace KeyLoad.SiteTests.BenchmarkComparisons;

internal sealed record SiteCoverageLineCoverage(IReadOnlySet<int> Executable, IReadOnlySet<int> Covered);
