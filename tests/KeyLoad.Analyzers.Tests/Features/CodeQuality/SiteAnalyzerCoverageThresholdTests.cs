using System.Text.Json;

namespace KeyLoad.Analyzers.Tests.Features.CodeQuality;

/// <summary>AC-CQ-009 and AC-BC-027: verify exact module, pipeline, overflow and coverage boundaries.</summary>
internal sealed class SiteAnalyzerCoverageThresholdTests
{
    [Test]
    public async Task ModuleLineThresholdUsesExactIntegerBoundaryAsync()
    {
        using var scope = new SiteAnalyzerCoverageTestScope();
        await scope.PrepareAsync();
        await SiteAnalyzerCoverageThresholdOracle.AssertModuleAsync(scope, SiteAnalyzerCoverageFixture.ModuleLineBoundary(SiteAnalyzerCoverageTokens.BelowLineBoundary), SiteAnalyzerCoverageTokens.ExpectedBelowLineCoverage, SiteAnalyzerCoverageTokens.ExpectedUniqueLineCount, SiteAnalyzerCoverageTokens.ExpectedDefaultBranchesCovered, SiteAnalyzerCoverageTokens.ExpectedDefaultBranchesValid, SiteAnalyzerCoverageThresholdOracle.BelowLinePipelineCounts);
        await SiteAnalyzerCoverageThresholdOracle.AssertModuleAsync(scope, SiteAnalyzerCoverageFixture.ModuleLineBoundary(SiteAnalyzerCoverageTokens.ExactLineBoundary), SiteAnalyzerCoverageTokens.ExpectedExactLineCoverage, SiteAnalyzerCoverageTokens.ExpectedUniqueLineCount, SiteAnalyzerCoverageTokens.ExpectedDefaultBranchesCovered, SiteAnalyzerCoverageTokens.ExpectedDefaultBranchesValid, SiteAnalyzerCoverageThresholdOracle.ExactLinePipelineCounts);
        await SiteAnalyzerCoverageThresholdOracle.AssertModuleAsync(scope, SiteAnalyzerCoverageFixture.ModuleLineBoundary(SiteAnalyzerCoverageTokens.AboveLineBoundary), SiteAnalyzerCoverageTokens.ExpectedAboveLineCoverage, SiteAnalyzerCoverageTokens.ExpectedUniqueLineCount, SiteAnalyzerCoverageTokens.ExpectedDefaultBranchesCovered, SiteAnalyzerCoverageTokens.ExpectedDefaultBranchesValid, SiteAnalyzerCoverageThresholdOracle.AboveLinePipelineCounts);
    }

    [Test]
    public async Task ModuleBranchThresholdUsesExactIntegerBoundaryAsync()
    {
        using var scope = new SiteAnalyzerCoverageTestScope();
        await scope.PrepareAsync();
        await SiteAnalyzerCoverageThresholdOracle.AssertModuleAsync(scope, SiteAnalyzerCoverageFixture.ModuleBranchBoundary(SiteAnalyzerCoverageTokens.BelowBranchBoundary, SiteAnalyzerCoverageTokens.BranchDenominator), SiteAnalyzerCoverageTokens.ExpectedUniqueLineCount, SiteAnalyzerCoverageTokens.ExpectedUniqueLineCount, SiteAnalyzerCoverageTokens.BelowBranchBoundary, SiteAnalyzerCoverageTokens.BranchDenominator, SiteAnalyzerCoverageThresholdOracle.FullPipelineCounts);
        await SiteAnalyzerCoverageThresholdOracle.AssertModuleAsync(scope, SiteAnalyzerCoverageFixture.ModuleBranchBoundary(SiteAnalyzerCoverageTokens.ExactBranchBoundary, SiteAnalyzerCoverageTokens.BranchDenominator), SiteAnalyzerCoverageTokens.ExpectedUniqueLineCount, SiteAnalyzerCoverageTokens.ExpectedUniqueLineCount, SiteAnalyzerCoverageTokens.ExactBranchBoundary, SiteAnalyzerCoverageTokens.BranchDenominator, SiteAnalyzerCoverageThresholdOracle.FullPipelineCounts);
        await SiteAnalyzerCoverageThresholdOracle.AssertModuleAsync(scope, SiteAnalyzerCoverageFixture.ModuleBranchBoundary(SiteAnalyzerCoverageTokens.AboveBranchBoundary, SiteAnalyzerCoverageTokens.BranchDenominator), SiteAnalyzerCoverageTokens.ExpectedUniqueLineCount, SiteAnalyzerCoverageTokens.ExpectedUniqueLineCount, SiteAnalyzerCoverageTokens.AboveBranchBoundary, SiteAnalyzerCoverageTokens.BranchDenominator, SiteAnalyzerCoverageThresholdOracle.FullPipelineCounts);
    }

    [Test]
    public async Task CriticalPipelineThresholdUsesExactIntegerBoundaryAsync()
    {
        using var scope = new SiteAnalyzerCoverageTestScope();
        await scope.PrepareAsync();
        await SiteAnalyzerCoverageThresholdOracle.AssertPipelineAsync(scope, SiteAnalyzerCoverageTokens.BelowPipelineBoundary);
        await SiteAnalyzerCoverageThresholdOracle.AssertPipelineAsync(scope, SiteAnalyzerCoverageTokens.ExactPipelineBoundary);
        await SiteAnalyzerCoverageThresholdOracle.AssertPipelineAsync(scope, SiteAnalyzerCoverageTokens.AbovePipelineBoundary);
    }

    [Test]
    public async Task HugeBranchThresholdUsesExactIntegerProductsAsync()
    {
        using var scope = new SiteAnalyzerCoverageTestScope();
        await scope.PrepareAsync();
        await SiteAnalyzerCoverageThresholdOracle.AssertModuleAsync(scope, SiteAnalyzerCoverageFixture.ModuleBranchBoundary(SiteAnalyzerCoverageTokens.BelowHugeBranchCovered, SiteAnalyzerCoverageTokens.HugeBranchDenominator), SiteAnalyzerCoverageTokens.ExpectedUniqueLineCount, SiteAnalyzerCoverageTokens.ExpectedUniqueLineCount, SiteAnalyzerCoverageTokens.BelowHugeBranchCovered, SiteAnalyzerCoverageTokens.HugeBranchDenominator, SiteAnalyzerCoverageThresholdOracle.FullPipelineCounts);
        await SiteAnalyzerCoverageThresholdOracle.AssertModuleAsync(scope, SiteAnalyzerCoverageFixture.ModuleBranchBoundary(SiteAnalyzerCoverageTokens.ExactHugeBranchCovered, SiteAnalyzerCoverageTokens.HugeBranchDenominator), SiteAnalyzerCoverageTokens.ExpectedUniqueLineCount, SiteAnalyzerCoverageTokens.ExpectedUniqueLineCount, SiteAnalyzerCoverageTokens.ExactHugeBranchCovered, SiteAnalyzerCoverageTokens.HugeBranchDenominator, SiteAnalyzerCoverageThresholdOracle.FullPipelineCounts);
    }

    [Test]
    public async Task AggregateCounterOverflowFailsBeforeNumericEvidenceAsync()
    {
        using var scope = new SiteAnalyzerCoverageTestScope();
        await scope.PrepareAsync();
        await scope.WriteFixtureAsync(SiteAnalyzerCoverageFixture.AggregateOverflow);
        var result = await scope.RunAsync(SiteAnalyzerCoverageTokens.ModeVerify);
        await Assert.That(result.ExitCode).IsNotEqualTo(SiteAnalyzerCoverageTokens.SuccessExitCode);
        using var report = SiteAnalyzerCoverageTestScope.ReadJson(scope.SummaryPath);
        await Assert.That(report.RootElement.GetProperty(SiteAnalyzerCoverageTokens.JsonSummaryProperty).ValueKind)
            .IsEqualTo(JsonValueKind.Null);
        await Assert.That(report.RootElement.GetProperty(SiteAnalyzerCoverageTokens.JsonPassedProperty).GetBoolean()).IsFalse();
        var failures = report.RootElement.GetProperty(SiteAnalyzerCoverageTokens.JsonFailuresProperty)
            .EnumerateArray().Select(static failure => failure.GetString()!).ToArray();
        await Assert.That(failures.Contains(SiteAnalyzerCoverageTokens.CounterOverflowFailure)).IsTrue();
    }
}
