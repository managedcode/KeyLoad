using System.Text.Json;

namespace KeyLoad.Analyzers.Tests.Features.CodeQuality;

/// <summary>AC-BC-027 and AC-CQ-009: native display precision never replaces integer branch evidence.</summary>
internal sealed class SiteAnalyzerCoverageBranchDisplayTests
{
    private const string DefaultCoveragePair = "70% (70/100)";
    private const int BranchRecordCount = SiteAnalyzerCoverageTokens.ExpectedSourceCount - SiteAnalyzerCoverageTokens.ExpectedDeclarationCount;
    private const int CoveragePercentScale = SiteAnalyzerCoverageTokens.CoveragePercentageScale;
    private const int BranchThresholdPercent = SiteAnalyzerCoverageTokens.RequiredModuleBranchPercent;
    private const long RoundedBoundaryCovered = 69995;
    private const long RoundedBoundaryTotal = 100000;
    private const string RoundedBoundaryDisplay = "70.00% (69995/100000)";
    private static readonly (string Display, long Covered, long Total)[] ObservedDisplays = [
        ("16.67% (1/6)", 1, 6),
        ("56.25% (9/16)", 9, 16),
        ("62.5% (5/8)", 5, 8),
        ("83.33% (5/6)", 5, 6),
        ("91.67% (11/12)", 11, 12)];
    private static readonly string[] InvalidDisplays = [
        "16.67 (1/6)",
        "16.67% (1/6",
        "16.67 % (1/6)",
        "16,67% (1/6)",
        "16.678% (1/6)",
        "+16.67% (1/6)",
        "1.667e1% (1/6)",
        "101% (1/6)",
        "100.01% (1/6)",
        "16.67%% (1/6)",
        "16.67% (1 /6)",
        "16.67% (+1/6)",
        "16.67% (1e0/6)",
        "16.67% (1/0)",
        "16.67% (7/6)",
        "16.67% (9223372036854775808/9223372036854775809)"];

    [Test]
    public async Task ObservedNativeDisplaysKeepIntegerBranchCountsAsync()
    {
        using var scope = new SiteAnalyzerCoverageTestScope();
        await scope.PrepareAsync();
        foreach (var sample in ObservedDisplays)
        {
            await scope.WriteFixtureAsync(CreateCoverageReport(sample.Display));
            var result = await scope.RunAsync(SiteAnalyzerCoverageTokens.ModeVerify);
            using var report = SiteAnalyzerCoverageTestScope.ReadJson(scope.SummaryPath);
            var module = report.RootElement.GetProperty(SiteAnalyzerCoverageTokens.JsonSummaryProperty);
            await Assert.That(module.ValueKind).IsEqualTo(JsonValueKind.Object);
            await Assert.That(module.GetProperty(SiteAnalyzerCoverageTokens.JsonBranchesCovered).GetInt64())
                .IsEqualTo(BranchRecordCount * sample.Covered);
            await Assert.That(module.GetProperty(SiteAnalyzerCoverageTokens.JsonBranchesValid).GetInt64())
                .IsEqualTo(BranchRecordCount * sample.Total);
            var expectedPass = sample.Covered * CoveragePercentScale >= sample.Total * BranchThresholdPercent;
            await Assert.That(report.RootElement.GetProperty(SiteAnalyzerCoverageTokens.JsonPassedProperty).GetBoolean())
                .IsEqualTo(expectedPass);
            await Assert.That(result.ExitCode == SiteAnalyzerCoverageTokens.SuccessExitCode).IsEqualTo(expectedPass);
        }
    }

    [Test]
    public async Task RoundedDisplayCannotChangeIntegerThresholdDecisionAsync()
    {
        using var scope = new SiteAnalyzerCoverageTestScope();
        await scope.PrepareAsync();
        await scope.WriteFixtureAsync(CreateCoverageReport(RoundedBoundaryDisplay));
        var result = await scope.RunAsync(SiteAnalyzerCoverageTokens.ModeVerify);
        using var report = SiteAnalyzerCoverageTestScope.ReadJson(scope.SummaryPath);
        var root = report.RootElement;
        var module = root.GetProperty(SiteAnalyzerCoverageTokens.JsonSummaryProperty);
        await Assert.That(module.GetProperty(SiteAnalyzerCoverageTokens.JsonBranchesCovered).GetInt64())
            .IsEqualTo(BranchRecordCount * RoundedBoundaryCovered);
        await Assert.That(module.GetProperty(SiteAnalyzerCoverageTokens.JsonBranchesValid).GetInt64())
            .IsEqualTo(BranchRecordCount * RoundedBoundaryTotal);
        await Assert.That(root.GetProperty(SiteAnalyzerCoverageTokens.JsonPassedProperty).GetBoolean()).IsFalse();
        await Assert.That(result.ExitCode).IsNotEqualTo(SiteAnalyzerCoverageTokens.SuccessExitCode);
        var failures = root.GetProperty(SiteAnalyzerCoverageTokens.JsonFailuresProperty)
            .EnumerateArray().Select(static failure => failure.GetString()!).ToArray();
        await Assert.That(failures.Contains(SiteAnalyzerCoverageTokens.ModuleBranchFailure)).IsTrue();
    }

    [Test]
    public async Task MalformedDisplayAndUnsignedPairTokensFailClosedAsync()
    {
        using var scope = new SiteAnalyzerCoverageTestScope();
        await scope.PrepareAsync();
        foreach (var invalidDisplay in InvalidDisplays)
        {
            await scope.WriteFixtureAsync(CreateCoverageReport(invalidDisplay));
            var result = await scope.RunAsync(SiteAnalyzerCoverageTokens.ModeVerify);
            using var report = SiteAnalyzerCoverageTestScope.ReadJson(scope.SummaryPath);
            await Assert.That(result.ExitCode).IsNotEqualTo(SiteAnalyzerCoverageTokens.SuccessExitCode);
            await Assert.That(report.RootElement.GetProperty(SiteAnalyzerCoverageTokens.JsonSummaryProperty).ValueKind)
                .IsEqualTo(JsonValueKind.Null);
            await Assert.That(report.RootElement.GetProperty(SiteAnalyzerCoverageTokens.JsonFailuresProperty).GetArrayLength())
                .IsNotEqualTo(0);
        }
    }

    private static string CreateCoverageReport(string displayAndPair) =>
        SiteAnalyzerCoverageXmlBuilder.Build().Replace(DefaultCoveragePair, displayAndPair, StringComparison.Ordinal);
}
