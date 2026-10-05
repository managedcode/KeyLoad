using System.Numerics;
using System.Text.Json;

namespace KeyLoad.Analyzers.Tests.Features.CodeQuality;

internal static class SiteAnalyzerCoverageThresholdOracle
{
    private static readonly CoveragePipeline[] Pipelines = ReadPipelines();
    internal static readonly long[] FullPipelineCounts = GetPipelineLineCounts(null);
    internal static readonly long[] BelowLinePipelineCounts = GetPipelineLineCounts(SiteAnalyzerCoverageTokens.BelowLineBoundary);
    internal static readonly long[] ExactLinePipelineCounts = GetPipelineLineCounts(SiteAnalyzerCoverageTokens.ExactLineBoundary);
    internal static readonly long[] AboveLinePipelineCounts = GetPipelineLineCounts(SiteAnalyzerCoverageTokens.AboveLineBoundary);

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
        var expectedPipelineCounts = GetPipelineLineCounts(null);
        var endpointPipelineIndex = Array.FindIndex(Pipelines, static pipeline =>
            pipeline.Diagnostic == SiteAnalyzerCoverageTokens.CriticalPipelineId);
        expectedPipelineCounts[endpointPipelineIndex] = coveredLineCount;
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
        await Assert.That(Pipelines.Length).IsEqualTo(SiteAnalyzerCoverageTokens.ExpectedPipelineCount);
        var failures = new List<string>();
        for (var index = 0; index < rows.Length; index++)
        {
            var row = rows[index];
            var covered = expectedCoveredCounts[index];
            var total = Pipelines[index].LineCount;
            var passed = (long)covered * SiteAnalyzerCoverageTokens.CoveragePercentageScale >= (long)total * SiteAnalyzerCoverageTokens.RequiredPipelinePercent;
            await Assert.That(row.GetProperty(SiteAnalyzerCoverageTokens.JsonDiagnostic).GetString()).IsEqualTo(Pipelines[index].Diagnostic);
            await Assert.That(row.GetProperty(SiteAnalyzerCoverageTokens.JsonLinesCovered).GetInt64()).IsEqualTo(covered);
            await Assert.That(row.GetProperty(SiteAnalyzerCoverageTokens.JsonLinesValid).GetInt64()).IsEqualTo(total);
            await Assert.That(row.GetProperty(SiteAnalyzerCoverageTokens.JsonPassed).GetBoolean()).IsEqualTo(passed);
            if (!passed)
            { failures.Add(Pipelines[index].Diagnostic); }
        }

        return failures.ToArray();
    }

    private static string[] ReadFailures(JsonElement report) => report.GetProperty(SiteAnalyzerCoverageTokens.JsonFailuresProperty)
        .EnumerateArray().Select(static failure => failure.GetString()!).ToArray();

    private static CoveragePipeline[] ReadPipelines()
    {
        var contractPath = Path.Combine(
            SiteAnalyzerCoverageXmlBuilder.FindRepositoryRoot(),
            SiteAnalyzerCoverageTokens.ContractRelativePath);
        using var contract = JsonDocument.Parse(File.ReadAllText(contractPath));
        var executableSources = contract.RootElement.GetProperty(SiteAnalyzerCoverageTokens.JsonSources)
            .EnumerateArray()
            .Where(static source => source.GetProperty(SiteAnalyzerCoverageTokens.ClassificationProperty).GetString() == SiteAnalyzerCoverageTokens.ExecutableKind)
            .Select(static source => source.GetProperty(SiteAnalyzerCoverageTokens.PathProperty).GetString()!)
            .ToHashSet(StringComparer.Ordinal);
        return contract.RootElement.GetProperty(SiteAnalyzerCoverageTokens.JsonPipelines)
            .EnumerateArray()
            .Select(pipeline => new CoveragePipeline(
                pipeline.GetProperty(SiteAnalyzerCoverageTokens.JsonDiagnostic).GetString()!,
                pipeline.GetProperty(SiteAnalyzerCoverageTokens.JsonSources).EnumerateArray()
                    .Select(static source => source.GetString()!)
                    .Select(path =>
                    {
                        if (!executableSources.Contains(path))
                        {
                            throw new InvalidOperationException($"Pipeline source is not executable in the frozen inventory: {path}");
                        }

                        return path;
                    })
                    .ToArray()))
            .ToArray();
    }

    private static long[] GetPipelineLineCounts(int? moduleCoveredLinePercent)
    {
        var executableSources = ReadExecutableSources();
        var coveredLines = new Dictionary<string, int>(StringComparer.Ordinal);
        if (moduleCoveredLinePercent is not null)
        {
            var totalCovered = executableSources.Length * SiteAnalyzerCoverageTokens.LinesPerSource * moduleCoveredLinePercent.Value /
                SiteAnalyzerCoverageTokens.CoveragePercentageScale;
            if (executableSources.Contains(SiteAnalyzerCoverageTokens.CriticalPipelineSource, StringComparer.Ordinal))
            {
                var protectedLines = Math.Min(SiteAnalyzerCoverageTokens.LinesPerSource, totalCovered);
                coveredLines.Add(SiteAnalyzerCoverageTokens.CriticalPipelineSource, protectedLines);
                totalCovered -= protectedLines;
            }

            foreach (var source in executableSources)
            {
                if (coveredLines.ContainsKey(source))
                {
                    continue;
                }

                var coveredHere = Math.Min(SiteAnalyzerCoverageTokens.LinesPerSource, totalCovered);
                coveredLines.Add(source, coveredHere);
                totalCovered -= coveredHere;
            }
        }

        return Pipelines.Select(pipeline => pipeline.Sources.Sum(source => (long)(
            moduleCoveredLinePercent is null
                ? SiteAnalyzerCoverageTokens.LinesPerSource
                : coveredLines[source]))).ToArray();
    }

    private static string[] ReadExecutableSources()
    {
        var contractPath = Path.Combine(
            SiteAnalyzerCoverageXmlBuilder.FindRepositoryRoot(),
            SiteAnalyzerCoverageTokens.ContractRelativePath);
        using var contract = JsonDocument.Parse(File.ReadAllText(contractPath));
        return contract.RootElement.GetProperty(SiteAnalyzerCoverageTokens.JsonSources)
            .EnumerateArray()
            .Where(static source => source.GetProperty(SiteAnalyzerCoverageTokens.ClassificationProperty).GetString() == SiteAnalyzerCoverageTokens.ExecutableKind)
            .Select(static source => source.GetProperty(SiteAnalyzerCoverageTokens.PathProperty).GetString()!)
            .ToArray();
    }

    private sealed record CoveragePipeline(string Diagnostic, string[] Sources)
    {
        internal long LineCount => Sources.Length * SiteAnalyzerCoverageTokens.LinesPerSource;
    }
}
