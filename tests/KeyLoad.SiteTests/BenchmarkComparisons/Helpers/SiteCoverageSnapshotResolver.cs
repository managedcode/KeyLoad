namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed record SiteCoverageBranchTotals(int Outcomes, int CoveredOutcomes);

internal static class SiteCoverageSnapshotResolver
{
    public static bool IsCovered(int offset, IReadOnlyList<SiteCoverageScriptSnapshot> snapshots)
    {
        foreach (var snapshot in snapshots)
        {
            if (SnapshotCoversOffset(snapshot, offset))
            {
                return true;
            }
        }

        return false;
    }

    public static SiteCoverageBranchTotals CountBranches(IReadOnlyList<SiteCoverageScriptSnapshot> snapshots)
    {
        var observed = CollectObservedBranches(snapshots);
        var covered = observed.Count(branch => IsBranchCovered(branch, snapshots));
        return new(observed.Count, covered);
    }

    private static HashSet<SiteCoverageBranchKey> CollectObservedBranches(
        IReadOnlyList<SiteCoverageScriptSnapshot> snapshots)
    {
        var observed = new HashSet<SiteCoverageBranchKey>();
        foreach (var function in snapshots.SelectMany(snapshot => snapshot.Functions)
                     .Where(function => function.IsBlockCoverage))
        {
            foreach (var range in function.Ranges.Skip(SiteCoverageTokens.One))
            {
                var key = SiteCoverageBranchKey.From(function, range);
                observed.Add(key);
            }
        }

        return observed;
    }

    private static bool IsBranchCovered(SiteCoverageBranchKey branch,
        IReadOnlyList<SiteCoverageScriptSnapshot> snapshots)
    {
        foreach (var snapshot in snapshots)
        {
            var physicalFunction = snapshot.Functions.LastOrDefault(branch.MatchesPhysicalFunction);
            if (physicalFunction is null)
            {
                continue;
            }

            var explicitRange = physicalFunction.Ranges.SingleOrDefault(range =>
                range.StartOffset == branch.StartOffset && range.EndOffset == branch.EndOffset);
            if (explicitRange is not null)
            {
                if (explicitRange.Count > SiteCoverageTokens.Zero)
                {
                    return true;
                }

                continue;
            }

            if (ProvesPositiveThroughout(physicalFunction, branch.StartOffset, branch.EndOffset))
            {
                return true;
            }
        }

        return false;
    }

    private static bool SnapshotCoversOffset(SiteCoverageScriptSnapshot snapshot, int offset)
    {
        var eligible = snapshot.Functions.Where(function => Contains(function.FunctionStart, function.FunctionEnd, offset)).ToArray();
        if (eligible.Length == SiteCoverageTokens.Zero)
        {
            return false;
        }

        var deepestWidth = eligible.Min(function => function.FunctionEnd - function.FunctionStart);
        var selected = eligible.Last(function => function.FunctionEnd - function.FunctionStart == deepestWidth);
        var effective = EffectiveRangeAt(selected, offset);
        return effective is not null && effective.Count > SiteCoverageTokens.Zero;
    }

    private static bool ProvesPositiveThroughout(SiteCoverageFunctionRanges function, int start, int end)
    {
        if (start < function.FunctionStart || end > function.FunctionEnd)
        {
            return false;
        }

        var boundaries = function.Ranges.SelectMany(range => new[] { range.StartOffset, range.EndOffset })
            .Where(offset => offset > start && offset < end).Append(start).Append(end)
            .Distinct().Order().ToArray();
        for (var index = SiteCoverageTokens.Zero; index + SiteCoverageTokens.One < boundaries.Length; index++)
        {
            var effective = EffectiveRangeAt(function, boundaries[index]);
            if (effective is null || effective.Count <= SiteCoverageTokens.Zero)
            {
                return false;
            }
        }

        return boundaries.Length > SiteCoverageTokens.One;
    }

    private static SiteCoverageRange? EffectiveRangeAt(SiteCoverageFunctionRanges function, int offset) =>
        function.Ranges.Where(range => Contains(range.StartOffset, range.EndOffset, offset))
            .MinBy(range => range.EndOffset - range.StartOffset);

    private static bool Contains(int start, int end, int offset) => offset >= start && offset < end;
}

internal sealed record SiteCoverageBranchKey(string SourcePath, int FunctionStart, int FunctionEnd,
    int StartOffset, int EndOffset)
{
    public static SiteCoverageBranchKey From(SiteCoverageFunctionRanges function, SiteCoverageRange range) =>
        new(function.SourcePath, function.FunctionStart, function.FunctionEnd, range.StartOffset, range.EndOffset);

    public bool MatchesPhysicalFunction(SiteCoverageFunctionRanges function) =>
        function.SourcePath == SourcePath && function.FunctionStart == FunctionStart && function.FunctionEnd == FunctionEnd;
}
