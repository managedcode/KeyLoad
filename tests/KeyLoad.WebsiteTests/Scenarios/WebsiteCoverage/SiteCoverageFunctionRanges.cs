namespace KeyLoad.WebsiteTests.BenchmarkComparisons;

internal sealed record SiteCoverageFunctionRanges(string SourcePath, string FunctionName, bool IsBlockCoverage,
    int FunctionStart, int FunctionEnd, IReadOnlyList<SiteCoverageRange> Ranges);
