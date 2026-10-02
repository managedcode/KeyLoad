namespace KeyLoad.Analyzers.Tests.Features.CodeQuality;

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
            : sources.Length * linesPerSource * coveredLinePercent.Value / SiteAnalyzerCoverageTokens.CoveragePercentageScale;
        ProtectPipelineSource(sources, linesPerSource, coveredLinePercent, result, ref totalCovered);
        DistributeLines(sources, linesPerSource, coveredLinePercent, partialSource, partialCoveredLines, result, ref totalCovered);
        return result;
    }

    private static void ProtectPipelineSource(
        string[] sources,
        int linesPerSource,
        int? coveredLinePercent,
        Dictionary<string, int?> result,
        ref int totalCovered)
    {
        if (coveredLinePercent is null || !sources.Contains(SiteAnalyzerCoverageTokens.CriticalPipelineSource, StringComparer.Ordinal))
        { return; }
        var protectedLines = Math.Min(linesPerSource, totalCovered);
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
            { continue; }
            if (coveredLinePercent is not null)
            {
                var coveredHere = Math.Min(linesPerSource, totalCovered);
                result.Add(source, coveredHere);
                totalCovered -= coveredHere;
                continue;
            }

            if (partialSource is not null)
            {
                result.Add(source, source == partialSource ? partialCoveredLines : linesPerSource);
                continue;
            }

            result.Add(source, null);
        }
    }
}
