using System.Text.Json.Nodes;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>Exercises the actual pure validator with bounded controlled schema inputs.</summary>
[NotInParallel]
internal sealed class RawStorageReportValidatorTests
{
    private const string ValidScenario = "valid";
    private const string SentinelValue = "SENSITIVE_SENTINEL";
    private const int OversizedRequestCharacters = 64 * 1024;
    private static readonly string[] RejectionScenarios =
    [
        "missing-entry",
        "extra-entry",
        "duplicate-entry",
        "wrong-engine",
        "wrong-payload",
        "wrong-count",
        "null-statistics",
        "bad-n-zero",
        "bad-n-high",
        "bad-original-count",
        "nonfinite-mean",
        "nonfinite-nanoseconds",
        "nonfinite-actual-nanoseconds",
        "zero-result-operations",
        "incomplete-actual-rows",
        "duplicate-actual-iteration",
        "retained-n-mismatch",
        "cross-stage-launch",
        "missing-memory",
        "negative-allocation",
        "zero-memory-operations",
        "four-result-rows",
        "six-result-rows",
        "duplicate-iteration",
        "multiple-launches",
        "wrong-version",
        "wrong-runtime",
        "wrong-runtime-major-boundary",
        "malformed-runtime-version",
        "wrong-namespace",
        "wrong-type",
        "wrong-method",
        "duplicate-parameter",
        "extra-parameter",
        "sentinel-error",
        "wrong-engine-argument"
    ];

    [Test]
    [Arguments("zonetree")]
    [Arguments("tsavorite")]
    public async Task AcGe005AcceptsCompleteEightCellReportWithOutlierReducedStatistics(string engine)
    {
        var report = RawStorageReportData.CreateValidReport(engine);
        var benchmarks = report[RawStorageReportData.BenchmarksKey]!.AsArray();
        await Assert.That(benchmarks.Count).IsEqualTo(8);
        foreach (var benchmark in benchmarks)
        {
            await Assert.That(benchmark![RawStorageReportData.StatisticsKey]![RawStorageReportData.SampleCountKey]!
                .GetValue<int>()).IsEqualTo(3);
            var measurements = benchmark[RawStorageReportData.MeasurementsKey]!.AsArray();
            var actualRows = measurements.Where(IsWorkloadActual).ToArray();
            var resultRows = measurements.Where(IsWorkloadResult).ToArray();
            await Assert.That(actualRows.Length).IsEqualTo(5);
            await Assert.That(resultRows.Length).IsEqualTo(3);
            await Assert.That(measurements.Count).IsEqualTo(9);
        }

        var response = await RawStorageReportNodeProcess.ValidateAsync(report, engine, [ValidScenario],
            TestContext.Current!.Execution.CancellationToken);
        await Assert.That(response.ExitCode).IsEqualTo(0);
        await Assert.That(response.StandardError).IsEqualTo(string.Empty);
        await Assert.That(response.Cases.Length).IsEqualTo(1);
        await Assert.That(response.Cases[0].Name).IsEqualTo(ValidScenario);
        await Assert.That(response.Cases[0].Accepted).IsTrue();
        await Assert.That(response.Cases[0].Unchanged).IsTrue();
    }

    [Test]
    [Arguments(RawStorageReportData.PlainRuntimeVersion)]
    [Arguments(RawStorageReportData.DetailedRuntimeVersion)]
    public async Task AcGe005AcceptsPinnedExporterPlainAndDetailedRuntimeVersion(string runtimeVersion)
    {
        var report = RawStorageReportData.CreateValidReport(runtimeVersion: runtimeVersion);
        var response = await RawStorageReportNodeProcess.ValidateAsync(report, RawStorageReportData.Engine,
            [ValidScenario], TestContext.Current!.Execution.CancellationToken);
        await Assert.That(response.ExitCode).IsEqualTo(0);
        await Assert.That(response.StandardError).IsEqualTo(string.Empty);
        await Assert.That(response.Cases.Single().Accepted).IsTrue();
        await Assert.That(response.Cases.Single().Unchanged).IsTrue();
    }

    [Test]
    public async Task AcGe005RejectsOversizedControlledRequestBeforeChildLaunch()
    {
        var report = RawStorageReportData.CreateValidReport(
            runtimeVersion: new string('x', OversizedRequestCharacters));
        await Assert.That(async () =>
        {
            await RawStorageReportNodeProcess.ValidateAsync(report, RawStorageReportData.Engine,
                [ValidScenario], TestContext.Current!.Execution.CancellationToken);
        }).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task AcGe005AcceptsInclusiveStatisticsSampleCountBoundaries()
    {
        foreach (var engine in new[] { "zonetree", "tsavorite" })
        {
            foreach (var sampleCount in new[] { 1, 5 })
            {
                var report = RawStorageReportData.CreateValidReport(engine, sampleCount);
                var response = await RawStorageReportNodeProcess.ValidateAsync(report, engine, [ValidScenario],
                    TestContext.Current!.Execution.CancellationToken);
                await Assert.That(response.ExitCode).IsEqualTo(0);
                await Assert.That(response.StandardError).IsEqualTo(string.Empty);
                await Assert.That(response.Cases.Single().Accepted).IsTrue();
                await Assert.That(response.Cases.Single().Unchanged).IsTrue();
            }
        }
    }

    [Test]
    public async Task AcGe005RejectsMissingExtraDuplicateAndMismatchedCellsAndEvidence()
    {
        var response = await RawStorageReportNodeProcess.ValidateAsync(
            RawStorageReportData.CreateValidReport(), RawStorageReportData.Engine, RejectionScenarios,
            TestContext.Current!.Execution.CancellationToken);
        await Assert.That(response.ExitCode).IsEqualTo(0);
        await Assert.That(response.StandardError).IsEqualTo(string.Empty);
        var actualNames = response.Cases.Select(result => result.Name).ToArray();
        await Assert.That(actualNames).IsEquivalentTo(RejectionScenarios, CollectionOrdering.Matching);
        foreach (var result in response.Cases)
        {
            await Assert.That(result.Accepted).IsFalse();
            await Assert.That(result.Unchanged).IsTrue();
            await Assert.That(string.IsNullOrWhiteSpace(result.Error)).IsFalse();
            await Assert.That(result.Error.Contains(SentinelValue, StringComparison.Ordinal)).IsFalse();
        }
    }

    private static bool IsWorkloadResult(JsonNode? measurement)
        => measurement?[RawStorageReportData.IterationModeKey]?.GetValue<string>() == "Workload"
            && measurement[RawStorageReportData.IterationStageKey]?.GetValue<string>() == "Result";

    private static bool IsWorkloadActual(JsonNode? measurement)
        => measurement?[RawStorageReportData.IterationModeKey]?.GetValue<string>() == "Workload"
            && measurement[RawStorageReportData.IterationStageKey]?.GetValue<string>() == "Actual";
}
