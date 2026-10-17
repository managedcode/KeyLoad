namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed record SiteCoverageLineCoverage(IReadOnlySet<int> Executable, IReadOnlySet<int> Covered);
