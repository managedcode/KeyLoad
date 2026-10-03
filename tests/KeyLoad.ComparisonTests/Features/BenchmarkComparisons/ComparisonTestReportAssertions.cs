using System.Text.Json;
using KeyLoad.Comparisons;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class ComparisonTestReportAssertions
{
    private const string FailedStatus = "failed";
    private const string MeasuredStatus = "measured";
    private const string KeyLoadTargetName = "KeyLoad";
    private const string Neo4jTargetName = "Neo4j";
    private const string PostgresTargetName = "PostgreSQL + pgvector";
    private const string QdrantTargetName = "Qdrant";
    private const string RabbitTargetName = "RabbitMQ";
    private const string RedisTargetName = "Redis";
    private const int ExpectedTargetCount = 6;
    private const int ScenariosPerTarget = 8;
    private const int MeasuredCasesPerRepetition = 24;

    internal static async Task<ComparisonReport> VerifyAsync(string output, ComparisonOptions options,
        CancellationToken cancellationToken)
    {
        var reportPath = Path.Combine(output, "results.json");
        var json = await File.ReadAllTextAsync(reportPath, cancellationToken);
        var report = JsonSerializer.Deserialize<ComparisonReport>(json, ReportWriter.JsonOptions)!;
        await ComparisonReportIdentityAssertions.VerifyAsync(report, cancellationToken);
        await VerifyReportAsync(report, options);
        await VerifyCsvAsync(output, options, cancellationToken);
        return report;
    }

    private static async Task VerifyReportAsync(ComparisonReport report, ComparisonOptions options)
    {
        await Assert.That(report.SchemaVersion).IsEqualTo(3);
        await Assert.That(report.Targets.Length).IsEqualTo(ExpectedTargetCount);
        await Assert.That(report.Cases.Length).IsEqualTo(ExpectedTargetCount * ScenariosPerTarget * options.Repetitions);
        await VerifyCaseMatrixAsync(report, options);
        await Assert.That(report.Cases).DoesNotContain(item => item.Status == FailedStatus);
        await Assert.That(report.Cases.Count(item => item.Status == MeasuredStatus)).IsEqualTo(MeasuredCasesPerRepetition * options.Repetitions);
        await VerifyMeasuredCasesAsync(report, options);
        await Assert.That(report.Targets).Contains(target => target.Name == KeyLoadTargetName
            && target.Topology.Contains("RF3", StringComparison.Ordinal));
        await Assert.That(report.Targets).Contains(target => target.Name == Neo4jTargetName && target.Version != "unverified");
        await Assert.That(report.Cases.Count(item => item.Scenario == Scenario.GraphTraverse && item.Status == MeasuredStatus))
            .IsEqualTo(3 * options.Repetitions);
        await VerifyExternalImagesAsync(report);
    }

    private static async Task VerifyCaseMatrixAsync(ComparisonReport report, ComparisonOptions options)
    {
        string[] targets = [KeyLoadTargetName, PostgresTargetName, QdrantTargetName,
            RabbitTargetName, RedisTargetName, Neo4jTargetName];
        Scenario[] scenarios = [Scenario.PointRead, Scenario.DocumentWrite, Scenario.VectorExact,
            Scenario.QueueCycle, Scenario.GraphNeighbors, Scenario.GraphTraverse, Scenario.StreamAppend, Scenario.StreamRead];
        await Assert.That(report.Targets.Select(target => target.Name).ToHashSet().SetEquals(targets)).IsTrue();
        var expected = from target in targets
                       from scenario in scenarios
                       from repetition in Enumerable.Range(0, options.Repetitions)
                       select (target, scenario, repetition);
        var actual = report.Cases.Select(item => (item.Target, item.Scenario, item.Repetition)).ToHashSet();
        await Assert.That(actual.Count).IsEqualTo(report.Cases.Length);
        await Assert.That(actual.SetEquals(expected)).IsTrue();
    }

    private static async Task VerifyMeasuredCasesAsync(ComparisonReport report, ComparisonOptions options)
    {
        foreach (var item in report.Cases.Where(item => item.Status == MeasuredStatus))
        {
            await Assert.That(item.Measurement!.Successes).IsEqualTo(options.Operations);
            await Assert.That(item.Measurement.ClientResources).IsNotNull();
            if (item.Scenario == Scenario.QueueCycle)
            {
                await Assert.That(item.Measurement.UniqueCompletedMessages).IsEqualTo(options.Operations);
                await Assert.That(item.Measurement.Enqueue).IsNotNull();
                await Assert.That(item.Measurement.Receive).IsNotNull();
                await Assert.That(item.Measurement.Ack).IsNotNull();
            }
        }
    }

    private static async Task VerifyExternalImagesAsync(ComparisonReport report)
    {
        foreach (var target in report.Targets.Where(target => target.Name != KeyLoadTargetName))
        {
            await Assert.That(target.Image).Contains("@sha256:");
        }
    }

    private static async Task VerifyCsvAsync(string output, ComparisonOptions options, CancellationToken cancellationToken)
    {
        var lines = await File.ReadAllLinesAsync(Path.Combine(output, "samples.csv"), cancellationToken);
        await Assert.That(lines.Length).IsEqualTo(1 + MeasuredCasesPerRepetition * options.Repetitions * options.Operations);
    }
}
