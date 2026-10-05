using System.Numerics;
using System.Text.Json;

namespace KeyLoad.Analyzers.Tests.Features.CodeQuality;

internal static class SiteAnalyzerCoverageThresholdOracle
{
    private const int EndpointPipelineIndex = 1;
    private static readonly string[] PipelineIds = [
        "KLD0001", SiteAnalyzerCoverageTokens.CriticalPipelineId, "KLD0014", "KLD0020", "KLD0021", "KLD0022",
        "KLD0023", "KLD0024", "KLD0030", "KLD0031", "KLD0032", "KLD0033", "KLD0034"];
    private static readonly long[] PipelineLineTotals = [40, 10, 10, 20, 20, 10, 40, 10, 30, 50, 40, 40, 10];
    internal static readonly long[] FullPipelineCounts = PipelineLineTotals;
    internal static readonly long[] BelowLinePipelineCounts = [30, 10, 10, 20, 10, 10, 30, 0, 20, 30, 30, 30, 5];
    internal static readonly long[] ExactLinePipelineCounts = [30, 10, 10, 20, 10, 10, 30, 0, 20, 30, 30, 30, 8];
    internal static readonly long[] AboveLinePipelineCounts = [30, 10, 10, 20, 10, 10, 30, 0, 20, 30, 30, 30, 10];

    internal static async Task AssertModuleAsync(
        SiteAnalyzerCoverageTestScope scope,
        string fixture,
        long linesCovered,
        long linesValid,
        long branchesCovered,
        long branchesValid,
        long[] pipelineCounts)
    {
        await scope.WriteFixtureAsync(fixture);
        var result = await scope.RunAsync(SiteAnalyzerCoverageTokens.ModeVerify);
        using var report = SiteAnalyzerCoverageTestScope.ReadJson(scope.SummaryPath);
        await AssertEvidenceAsync(result, report.RootElement, linesCovered, linesValid, branchesCovered, branchesValid, pipelineCounts);
    }

    internal static async Task AssertPipelineAsync(SiteAnalyzerCoverageTestScope scope, int coveredLineCount)
    {
        var expectedPipelineCounts = (long[])FullPipelineCounts.Clone();
        expectedPipelineCounts[EndpointPipelineIndex] = coveredLineCount;
        await scope.WriteFixtureAsync(SiteAnalyzerCoverageFixture.PipelineLineBoundary(coveredLineCount));
        var result = await scope.RunAsync(SiteAnalyzerCoverageTokens.ModeVerify);
        using var report = SiteAnalyzerCoverageTestScope.ReadJson(scope.SummaryPath);
        var linesCovered = SiteAnalyzerCoverageTokens.ExpectedUniqueLineCount - SiteAnalyzerCoverageTokens.LinesPerSource + coveredLineCount;
        await AssertEvidenceAsync(result, report.RootElement, linesCovered,
            SiteAnalyzerCoverageTokens.ExpectedUniqueLineCount,
            SiteAnalyzerCoverageTokens.ExpectedDefaultBranchesCovered,
            SiteAnalyzerCoverageTokens.ExpectedDefaultBranchesValid,
            expectedPipelineCounts);
    }

    private static async Task AssertEvidenceAsync(
        (int ExitCode, string StandardOutput, string StandardError) result,
        JsonElement report,
        long linesCovered,
        long linesValid,
        long branchesCovered,
        long branchesValid,
        long[] expectedPipelineCounts)
    {
        var module = report.GetProperty(SiteAnalyzerCoverageTokens.JsonSummaryProperty);
        await Assert.That(module.ValueKind).IsEqualTo(JsonValueKind.Object);
        await Assert.That(module.GetProperty(SiteAnalyzerCoverageTokens.JsonLinesCovered).GetInt64()).IsEqualTo(linesCovered);
        await Assert.That(module.GetProperty(SiteAnalyzerCoverageTokens.JsonLinesValid).GetInt64()).IsEqualTo(linesValid);
        await Assert.That(module.GetProperty(SiteAnalyzerCoverageTokens.JsonBranchesCovered).GetInt64()).IsEqualTo(branchesCovered);
        await Assert.That(module.GetProperty(SiteAnalyzerCoverageTokens.JsonBranchesValid).GetInt64()).IsEqualTo(branchesValid);
        var expectedFailures = new List<string>();
        if (!MeetsThreshold(linesCovered, linesValid, SiteAnalyzerCoverageTokens.RequiredModuleLinePercent))
        {
            expectedFailures.Add(SiteAnalyzerCoverageTokens.ModuleLineFailure);
        }

        if (!MeetsThreshold(branchesCovered, branchesValid, SiteAnalyzerCoverageTokens.RequiredModuleBranchPercent))
        {
            expectedFailures.Add(SiteAnalyzerCoverageTokens.ModuleBranchFailure);
        }

        expectedFailures.AddRange(await AssertPipelineEvidenceAsync(report, expectedPipelineCounts));
        var expectedPassed = expectedFailures.Count == 0;
        await Assert.That(report.GetProperty(SiteAnalyzerCoverageTokens.JsonPassedProperty).GetBoolean()).IsEqualTo(expectedPassed);
        await Assert.That(result.ExitCode == SiteAnalyzerCoverageTokens.SuccessExitCode).IsEqualTo(expectedPassed);
        await Assert.That(ReadFailures(report).SequenceEqual(expectedFailures)).IsTrue();
    }

    private static bool MeetsThreshold(long covered, long total, int threshold) =>
        new BigInteger(covered) * SiteAnalyzerCoverageTokens.CoveragePercentageScale >=
        new BigInteger(total) * threshold;

    private static async Task<string[]> AssertPipelineEvidenceAsync(JsonElement report, long[] expectedCoveredCounts)
    {
        var rows = report.GetProperty(SiteAnalyzerCoverageTokens.JsonCriticalPipelines).EnumerateArray().ToArray();
        await Assert.That(rows.Length).IsEqualTo(SiteAnalyzerCoverageTokens.ExpectedPipelineCount);
        await Assert.That(expectedCoveredCounts.Length).IsEqualTo(SiteAnalyzerCoverageTokens.ExpectedPipelineCount);
        var failures = new List<string>();
        for (var index = 0; index < rows.Length; index++)
        {
            var row = rows[index];
            var covered = expectedCoveredCounts[index];
            var total = PipelineLineTotals[index];
            var passed = (long)covered * SiteAnalyzerCoverageTokens.CoveragePercentageScale >= (long)total * SiteAnalyzerCoverageTokens.RequiredPipelinePercent;
            await Assert.That(row.GetProperty(SiteAnalyzerCoverageTokens.JsonDiagnostic).GetString()).IsEqualTo(PipelineIds[index]);
            await Assert.That(row.GetProperty(SiteAnalyzerCoverageTokens.JsonLinesCovered).GetInt64()).IsEqualTo(covered);
            await Assert.That(row.GetProperty(SiteAnalyzerCoverageTokens.JsonLinesValid).GetInt64()).IsEqualTo(total);
            await Assert.That(row.GetProperty(SiteAnalyzerCoverageTokens.JsonPassed).GetBoolean()).IsEqualTo(passed);
            if (!passed)
            { failures.Add(PipelineIds[index]); }
        }

        return failures.ToArray();
    }

    private static string[] ReadFailures(JsonElement report) => report.GetProperty(SiteAnalyzerCoverageTokens.JsonFailuresProperty)
        .EnumerateArray().Select(static failure => failure.GetString()!).ToArray();
}
