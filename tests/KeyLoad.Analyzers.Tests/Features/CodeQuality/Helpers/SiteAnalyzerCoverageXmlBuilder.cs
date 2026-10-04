using System.Text;
using System.Text.Json;

namespace KeyLoad.Analyzers.Tests.Features.CodeQuality;

internal static class SiteAnalyzerCoverageXmlBuilder
{
    internal static string Build(
        string moduleName = SiteAnalyzerCoverageTokens.ModuleName,
        string? omitSource = null,
        int lineHits = SiteAnalyzerCoverageTokens.CounterOne,
        long branchCovered = SiteAnalyzerCoverageTokens.DefaultBranchCovered,
        long branchTotal = SiteAnalyzerCoverageTokens.DefaultBranchTotal,
        bool includeBranch = true,
        int linesPerSource = SiteAnalyzerCoverageTokens.LinesPerSource,
        int? coveredLinePercent = null,
        string? partialSource = null,
        int partialCoveredLines = SiteAnalyzerCoverageTokens.CounterZero,
        string? sourceRoot = null,
        bool multipleSourceRoots = false,
        bool includeDuplicateClass = false,
        bool includeMethods = false,
        int branchSourceCount = int.MaxValue,
        bool nativeBranchBooleanCasing = false,
        bool includeFalseBranch = false,
        string? boundaryLineSource = null,
        int? boundaryLineNumber = null)
    {
        var contractPath = Path.Combine(FindRepositoryRoot(), SiteAnalyzerCoverageTokens.ContractRelativePath);
        var executable = GetExecutableSources(contractPath, omitSource);
        var coveredLines = SiteAnalyzerCoverageLineBudget.GetCoveredLinesBySource(executable, linesPerSource, coveredLinePercent, partialSource, partialCoveredLines);
        var xml = new StringBuilder(SiteAnalyzerCoverageTokens.XmlDeclaration);
        AppendPackageHeader(xml, sourceRoot, multipleSourceRoots);
        xml.Append(moduleName).Append(SiteAnalyzerCoverageTokens.CoveragePackageMiddle);
        AppendClasses(xml, executable, coveredLines, lineHits, branchCovered, branchTotal, linesPerSource,
            includeBranch, branchSourceCount, includeDuplicateClass, includeMethods, nativeBranchBooleanCasing,
            includeFalseBranch, boundaryLineSource, boundaryLineNumber);
        return xml.Append(SiteAnalyzerCoverageTokens.PackageClose).ToString();
    }

    private static void AppendPackageHeader(StringBuilder xml, string? sourceRoot, bool multipleSourceRoots)
    {
        if (sourceRoot is not null)
        {
            var escapedSourceRoot = System.Security.SecurityElement.Escape(sourceRoot);
            xml.Append(SiteAnalyzerCoverageTokens.CoveragePackagePrefixWithSource)
                .Append(escapedSourceRoot);
            if (multipleSourceRoots)
            {
                xml.Append(SiteAnalyzerCoverageTokens.SourceRepeatOpen).Append(escapedSourceRoot);
            }

            xml.Append(SiteAnalyzerCoverageTokens.SourceToPackage);
        }
        else
        { xml.Append(SiteAnalyzerCoverageTokens.CoveragePackagePrefix); }
    }

    private static void AppendClasses(
        StringBuilder xml,
        string[] executable,
        Dictionary<string, int?> coveredLines,
        int lineHits,
        long branchCovered,
        long branchTotal,
        int linesPerSource,
        bool includeBranch,
        int branchSourceCount,
        bool includeDuplicateClass,
        bool includeMethods,
        bool nativeBranchBooleanCasing,
        bool includeFalseBranch,
        string? boundaryLineSource,
        int? boundaryLineNumber)
    {
        foreach (var source in executable)
        {
            var sourceHasBranches = includeBranch && branchSourceCount > 0;
            var sourceBoundaryLine = source == boundaryLineSource ? boundaryLineNumber : null;
            AppendClassCoverage(xml, source, SiteAnalyzerCoverageTokens.ClassNameValue, lineHits, branchCovered, branchTotal, sourceHasBranches, linesPerSource, coveredLines[source], includeMethods, nativeBranchBooleanCasing, includeFalseBranch, sourceBoundaryLine);
            if (includeDuplicateClass && source == SiteAnalyzerCoverageTokens.HashFileRelativePath)
            {
                AppendClassCoverage(xml, source, SiteAnalyzerCoverageTokens.DuplicateClassNameValue, SiteAnalyzerCoverageTokens.CounterZero, branchCovered, branchTotal, false, linesPerSource, coveredLines[source], includeMethods, nativeBranchBooleanCasing, includeFalseBranch, sourceBoundaryLine);
            }

            if (sourceHasBranches)
            { branchSourceCount--; }
        }
    }

    internal static string FindRepositoryRoot()
    {
        var candidate = new DirectoryInfo(AppContext.BaseDirectory);
        while (candidate is not null && !File.Exists(Path.Combine(candidate.FullName, SiteAnalyzerCoverageTokens.SolutionFileName)))
        {
            candidate = candidate.Parent;
        }

        return candidate?.FullName ?? throw new DirectoryNotFoundException(SiteAnalyzerCoverageTokens.RepositoryUnavailableMessage);
    }

    internal static int GetPhysicalLineCount(string relativePath)
    {
        var path = Path.Combine(FindRepositoryRoot(), relativePath);
        return File.ReadAllLines(path).Length;
    }

    private static void AppendClassCoverage(
        StringBuilder xml,
        string source,
        string className,
        int lineHits,
        long branchCovered,
        long branchTotal,
        bool includeBranch,
        int linesPerSource,
        int? coveredLineCount,
        bool includeMethods,
        bool nativeBranchBooleanCasing,
        bool includeFalseBranch,
        int? boundaryLineNumber)
    {
        xml.Append(SiteAnalyzerCoverageTokens.ClassOpen).Append(className).Append(SiteAnalyzerCoverageTokens.ClassFileSeparator).Append(source).Append(SiteAnalyzerCoverageTokens.ClassMiddle);
        if (includeMethods)
        { xml.Append(SiteAnalyzerCoverageTokens.MethodsBlock); }
        xml.Append(SiteAnalyzerCoverageTokens.LinesOpen);
        for (var lineNumber = SiteAnalyzerCoverageTokens.FirstLineNumber; lineNumber <= linesPerSource; lineNumber++)
        {
            var hits = coveredLineCount is null
                ? lineHits
                : lineNumber <= coveredLineCount ? SiteAnalyzerCoverageTokens.CounterOne : SiteAnalyzerCoverageTokens.CounterZero;
            var outputLineNumber = lineNumber == linesPerSource && boundaryLineNumber is not null
                ? boundaryLineNumber.Value
                : lineNumber;
            AppendLineCoverage(
                xml,
                outputLineNumber,
                hits,
                branchCovered,
                branchTotal,
                includeBranch && lineNumber == SiteAnalyzerCoverageTokens.FirstLineNumber,
                nativeBranchBooleanCasing,
                includeFalseBranch);
        }

        xml.Append(SiteAnalyzerCoverageTokens.ClassClose);
    }

    private static void AppendLineCoverage(
        StringBuilder xml,
        int lineNumber,
        int hits,
        long branchCovered,
        long branchTotal,
        bool includeBranch,
        bool nativeBranchBooleanCasing,
        bool includeFalseBranch)
    {
        xml.Append(SiteAnalyzerCoverageTokens.LineOpen)
            .Append(lineNumber.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Append(SiteAnalyzerCoverageTokens.LineHits)
            .Append(hits.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (!includeBranch)
        {
            xml.Append(includeFalseBranch
                ? nativeBranchBooleanCasing ? SiteAnalyzerCoverageTokens.BranchFalseNativeSuffix : SiteAnalyzerCoverageTokens.BranchFalseSuffix
                : SiteAnalyzerCoverageTokens.LineClose);
            return;
        }

        xml.Append(nativeBranchBooleanCasing ? SiteAnalyzerCoverageTokens.BranchTrueNative : SiteAnalyzerCoverageTokens.BranchTrue)
            .Append(Percentage(branchCovered, branchTotal))
            .Append(SiteAnalyzerCoverageTokens.CoveragePairOpen)
            .Append(branchCovered.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Append(SiteAnalyzerCoverageTokens.PairSlash)
            .Append(branchTotal.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Append(SiteAnalyzerCoverageTokens.PairClose);
    }

    private static string Percentage(long covered, long total) => total == 0
        ? SiteAnalyzerCoverageTokens.ZeroPercent
        : System.Numerics.BigInteger.Divide(System.Numerics.BigInteger.Multiply(covered, 100), total)
            .ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string[] GetExecutableSources(string contractPath, string? omitSource)
    {
        using var contract = JsonDocument.Parse(File.ReadAllText(contractPath));
        return contract.RootElement.GetProperty(SiteAnalyzerCoverageTokens.JsonSources).EnumerateArray()
            .Where(source => source.GetProperty(SiteAnalyzerCoverageTokens.ClassificationProperty).GetString() == SiteAnalyzerCoverageTokens.ExecutableKind)
            .Select(source => source.GetProperty(SiteAnalyzerCoverageTokens.PathProperty).GetString()!)
            .Where(path => path != omitSource)
            .ToArray();
    }
}
