namespace KeyLoad.SiteTests.BenchmarkComparisons;

internal sealed record SiteCoverageFileResult(string Path, string Sha256, int ExecutableLines, int CoveredLines,
    int BlockOutcomes, int CoveredBlockOutcomes);
