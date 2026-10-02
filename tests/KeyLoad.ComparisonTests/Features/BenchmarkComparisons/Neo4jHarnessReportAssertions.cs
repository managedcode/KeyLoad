using KeyLoad.Comparisons;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class Neo4jHarnessReportAssertions
{
    private const string PointReadMismatch = "PointReadMismatch";

    public static async Task VerifySuccessfulRunAsync(ComparisonReport report)
    {
        await Assert.That(report.SchemaVersion == 3 && report.Options == Neo4jHarnessConstants.SmallOptions()).IsTrue();
        await Assert.That(report.Targets.Length == 1 && report.Targets[0].Name == Neo4jHarnessConstants.Neo4jTargetName
            && report.Targets[0].Version != "unverified").IsTrue();
        await Assert.That(report.Cases.Length == Neo4jHarnessConstants.TotalCaseCount).IsTrue();
        await Assert.That(report.Cases.Count(item => item.Status == Neo4jHarnessConstants.MeasuredStatus)
            == Neo4jHarnessConstants.SuccessfulCaseCount).IsTrue();
        await Assert.That(report.Cases.Count(item => item.Status == Neo4jHarnessConstants.UnsupportedStatus)
            == Neo4jHarnessConstants.UnsupportedScenarioCount * Neo4jHarnessConstants.SmallRepetitions).IsTrue();
        foreach (var item in report.Cases)
        {
            await AssertCaseAsync(item, IsSupported(item.Scenario));
        }
    }

    public static async Task VerifyMismatchRunAsync(ComparisonReport report, Neo4jHarnessMismatchObserver observer)
    {
        var expectedOptions = Neo4jHarnessConstants.SmallOptions() with { Warmup = 0, Repetitions = 1 };
        await Assert.That(observer.CorruptedBeforePointRead && observer.RestoredBeforeDocumentWrite).IsTrue();
        await Assert.That(report.SchemaVersion == 3 && report.Options == expectedOptions).IsTrue();
        await Assert.That(report.Cases.Length == Neo4jHarnessConstants.TotalCaseCount / Neo4jHarnessConstants.SmallRepetitions).IsTrue();
        foreach (var item in report.Cases)
        {
            if (item.Scenario == Scenario.PointRead)
            {
                await AssertPointReadMismatchAsync(item);
            }
            else
            {
                await AssertCaseAsync(item, IsSupported(item.Scenario));
            }
        }
    }

    private static async Task AssertCaseAsync(ComparisonCase item, bool supported)
    {
        if (!supported)
        {
            await Assert.That(item.Status == Neo4jHarnessConstants.UnsupportedStatus && item.Measurement is null && item.Samples.IsEmpty).IsTrue();
            return;
        }

        await Assert.That(item.Status == Neo4jHarnessConstants.MeasuredStatus && item.Measurement is not null
            && item.Measurement.Attempts == Neo4jHarnessConstants.SmallOperations
            && item.Measurement.Successes == Neo4jHarnessConstants.SmallOperations
            && item.Samples.Length == Neo4jHarnessConstants.SmallOperations
            && item.Samples.All(sample => sample.Success && sample.Error is null)).IsTrue();
    }

    private static async Task AssertPointReadMismatchAsync(ComparisonCase item)
    {
        await Assert.That(item.Status == Neo4jHarnessConstants.FailedStatus && item.Measurement is not null
            && item.Measurement.Attempts == Neo4jHarnessConstants.SmallOperations
            && item.Measurement.Failures == Neo4jHarnessConstants.SmallOperations
            && item.Measurement.Successes == 0 && item.Measurement.UsefulOperationsPerSecond == 0
            && item.Samples.Length == Neo4jHarnessConstants.SmallOperations
            && item.Samples.All(sample => !sample.Success && sample.Error == PointReadMismatch)).IsTrue();
    }

    private static bool IsSupported(Scenario scenario) => scenario is Scenario.PointRead or Scenario.DocumentWrite
        or Scenario.GraphNeighbors or Scenario.GraphTraverse;
}
