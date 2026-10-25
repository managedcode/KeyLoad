namespace KeyLoad.AnalyzerTests.CodeQuality;

internal static class SiteAnalyzerCoverageLineBudget
{
    internal static Dictionary<string, int?> GetCoveredLinesBySource(
        string[] sources,
        int linesPerSource,
        int? coveredLinePercent,
        string? partialSource,
        int partialCoveredLines)
    {
        var result = new Dictionary<string, int?>(StringComparer.Ordinal);
        var totalCovered = coveredLinePercent is null
            ? 0
            : sources.Sum(source => GetLineCount(source, linesPerSource)) * coveredLinePercent.Value / SiteAnalyzerCoverageTokens.CoveragePercentageScale;
        ProtectPipelineSource(sources, linesPerSource, coveredLinePercent, result, ref totalCovered);
        DistributeLines(sources, linesPerSource, coveredLinePercent, partialSource, partialCoveredLines, result, ref totalCovered);
        return result;
    }

    internal static int GetLineCount(string source, int linesPerSource) =>
        source == SiteAnalyzerCoverageTokens.ShortSourceRelativePath
            ? Math.Min(linesPerSource, SiteAnalyzerCoverageTokens.ShortSourceLines)
            : linesPerSource;

    private static void ProtectPipelineSource(
        string[] sources,
        int linesPerSource,
        int? coveredLinePercent,
        Dictionary<string, int?> result,
        ref int totalCovered)
    {
        if (coveredLinePercent is null || !sources.Contains(SiteAnalyzerCoverageTokens.CriticalPipelineSource, StringComparer.Ordinal))
        {
            return;
        }
        var protectedLines = Math.Min(GetLineCount(SiteAnalyzerCoverageTokens.CriticalPipelineSource, linesPerSource), totalCovered);
        result.Add(SiteAnalyzerCoverageTokens.CriticalPipelineSource, protectedLines);
        totalCovered -= protectedLines;
    }

    private static void DistributeLines(
        string[] sources,
        int linesPerSource,
        int? coveredLinePercent,
        string? partialSource,
        int partialCoveredLines,
        Dictionary<string, int?> result,
        ref int totalCovered)
    {
        foreach (var source in sources)
        {
            if (result.ContainsKey(source))
            {
                continue;
            }
            var sourceLines = GetLineCount(source, linesPerSource);
            if (coveredLinePercent is not null)
            {
                var coveredHere = Math.Min(sourceLines, totalCovered);
                result.Add(source, coveredHere);
                totalCovered -= coveredHere;
                continue;
            }

            if (partialSource is not null)
            {
                result.Add(source, source == partialSource ? partialCoveredLines : sourceLines);
                continue;
            }

            result.Add(source, null);
        }
    }
}
