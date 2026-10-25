namespace KeyLoad.WebsiteTests.BenchmarkComparisons;

internal sealed record SiteCoverageBranchKey(string SourcePath, int FunctionStart, int FunctionEnd,
    int StartOffset, int EndOffset)
{
    public static SiteCoverageBranchKey From(SiteCoverageFunctionRanges function, SiteCoverageRange range) =>
        new(function.SourcePath, function.FunctionStart, function.FunctionEnd, range.StartOffset, range.EndOffset);

    public bool MatchesPhysicalFunction(SiteCoverageFunctionRanges function) =>
        function.SourcePath == SourcePath && function.FunctionStart == FunctionStart && function.FunctionEnd == FunctionEnd;
}
