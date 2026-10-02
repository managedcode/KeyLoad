namespace KeyLoad.Analyzers.Tests.Features.CodeQuality;

internal static class SiteAnalyzerCoverageFixture
{
    internal static string Valid => SiteAnalyzerCoverageXmlBuilder.Build();
    internal static string UniqueSourceRoot => SiteAnalyzerCoverageXmlBuilder.Build(sourceRoot: SiteAnalyzerCoverageXmlBuilder.FindRepositoryRoot());
    internal static string ParentSourceRoot => SiteAnalyzerCoverageXmlBuilder.Build(sourceRoot: SiteAnalyzerCoverageTokens.ParentPathSegment);
    internal static string MultipleSourceRoots => SiteAnalyzerCoverageXmlBuilder.Build(sourceRoot: SiteAnalyzerCoverageXmlBuilder.FindRepositoryRoot(), multipleSourceRoots: true);
    internal static string UnknownSource => SiteAnalyzerCoverageXmlBuilder.Build().Replace(
        SiteAnalyzerCoverageTokens.HashFileRelativePath,
        SiteAnalyzerCoverageTokens.UnknownSourcePath,
        StringComparison.Ordinal);
    internal static string CrossClassLineUnion => SiteAnalyzerCoverageXmlBuilder.Build(lineHits: SiteAnalyzerCoverageTokens.CounterTwo, includeDuplicateClass: true);
    internal static string MethodsExcluded => SiteAnalyzerCoverageXmlBuilder.Build(includeMethods: true);
    internal static string Dtd => SiteAnalyzerCoverageXmlBuilder.Build().Replace(
        SiteAnalyzerCoverageTokens.XmlDeclaration,
        SiteAnalyzerCoverageTokens.XmlDeclaration + SiteAnalyzerCoverageTokens.DtdPrefix,
        StringComparison.Ordinal);
    internal static string MalformedInteger => SiteAnalyzerCoverageXmlBuilder.Build().Replace(
        SiteAnalyzerCoverageTokens.LineHits + SiteAnalyzerCoverageTokens.CounterOneText,
        SiteAnalyzerCoverageTokens.LineHits + SiteAnalyzerCoverageTokens.MalformedInteger,
        StringComparison.Ordinal);
    internal static string UnsafePath => SiteAnalyzerCoverageXmlBuilder.Build().Replace(
        SiteAnalyzerCoverageTokens.HashFileRelativePath,
        SiteAnalyzerCoverageTokens.UnsafePath,
        StringComparison.Ordinal);
    internal static string ConflictingLine => SiteAnalyzerCoverageXmlBuilder.Build().Replace(
        SiteAnalyzerCoverageTokens.LineClose,
        SiteAnalyzerCoverageTokens.DuplicateLinePathSuffix,
        StringComparison.Ordinal);
    internal static string ConflictingBranch => SiteAnalyzerCoverageXmlBuilder.Build().Replace(
        SiteAnalyzerCoverageTokens.PairClose,
        SiteAnalyzerCoverageTokens.DuplicateBranchSuffix,
        StringComparison.Ordinal);
    internal static string DuplicateBranchCaseVariant => SiteAnalyzerCoverageXmlBuilder.Build().Replace(
        SiteAnalyzerCoverageTokens.PairClose,
        SiteAnalyzerCoverageTokens.DuplicateBranchCaseVariantSuffix,
        StringComparison.Ordinal);
    internal static string NativeBranchBooleanCases => SiteAnalyzerCoverageXmlBuilder.Build(nativeBranchBooleanCasing: true, includeFalseBranch: true);
    internal static string MalformedBranchBoolean => SiteAnalyzerCoverageXmlBuilder.Build().Replace(
        SiteAnalyzerCoverageTokens.LowercaseTrue,
        SiteAnalyzerCoverageTokens.InvalidBranchBoolean,
        StringComparison.Ordinal);
    internal static string AggregateOverflow => SiteAnalyzerCoverageXmlBuilder.Build(
        branchCovered: SiteAnalyzerCoverageTokens.CounterZero,
        branchTotal: SiteAnalyzerCoverageTokens.MaximumCoverageCounter,
        branchSourceCount: SiteAnalyzerCoverageTokens.OverflowBranchSourceCount);
    internal static string ZeroLineNumber => SiteAnalyzerCoverageXmlBuilder.Build().Replace(
        SiteAnalyzerCoverageTokens.LineNumberOneAttribute,
        SiteAnalyzerCoverageTokens.LineNumberZeroAttribute,
        StringComparison.Ordinal);
    internal static string BeyondPhysicalLine => SiteAnalyzerCoverageXmlBuilder.Build(
        boundaryLineSource: SiteAnalyzerCoverageTokens.HashFileRelativePath,
        boundaryLineNumber: SiteAnalyzerCoverageXmlBuilder.GetPhysicalLineCount(SiteAnalyzerCoverageTokens.HashFileRelativePath) + SiteAnalyzerCoverageTokens.CounterOne);
    internal static string LastPhysicalLine => SiteAnalyzerCoverageXmlBuilder.Build(
        boundaryLineSource: SiteAnalyzerCoverageTokens.HashFileRelativePath,
        boundaryLineNumber: SiteAnalyzerCoverageXmlBuilder.GetPhysicalLineCount(SiteAnalyzerCoverageTokens.HashFileRelativePath));
    internal static string MissingModule => SiteAnalyzerCoverageXmlBuilder.Build(moduleName: SiteAnalyzerCoverageTokens.OtherModuleName);
    internal static string MissingSource => SiteAnalyzerCoverageXmlBuilder.Build(omitSource: SiteAnalyzerCoverageTokens.HashFileRelativePath);
    internal static string BelowThreshold => SiteAnalyzerCoverageXmlBuilder.Build(
        lineHits: SiteAnalyzerCoverageTokens.CounterZero,
        branchCovered: SiteAnalyzerCoverageTokens.CounterZero);
    internal static string EmptyBranches => SiteAnalyzerCoverageXmlBuilder.Build(includeBranch: false);
    internal static string ZeroBranchDenominator => SiteAnalyzerCoverageXmlBuilder.Build(
        branchCovered: SiteAnalyzerCoverageTokens.ZeroBranchDenominator,
        branchTotal: SiteAnalyzerCoverageTokens.ZeroBranchDenominator);
    internal static string ModuleLineBoundary(int coveredPercent) =>
        SiteAnalyzerCoverageXmlBuilder.Build(coveredLinePercent: coveredPercent);
    internal static string ModuleBranchBoundary(long covered, long total) =>
        SiteAnalyzerCoverageXmlBuilder.Build(branchCovered: covered, branchTotal: total, branchSourceCount: SiteAnalyzerCoverageTokens.SingleBranchSourceCount);
    internal static string PipelineLineBoundary(int coveredLineCount) =>
        SiteAnalyzerCoverageXmlBuilder.Build(
            partialSource: SiteAnalyzerCoverageTokens.CriticalPipelineSource,
            partialCoveredLines: coveredLineCount);
}
