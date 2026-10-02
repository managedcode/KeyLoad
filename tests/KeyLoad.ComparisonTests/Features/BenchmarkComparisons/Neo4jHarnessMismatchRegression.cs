using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class Neo4jHarnessMismatchRegression
{
    private const string SourceRevision = "comparison-harness-test";

    internal static async Task VerifyAsync(Uri endpoint, string password, string image, CancellationToken cancellationToken)
    {
        var options = Neo4jHarnessConstants.SmallOptions() with { Warmup = 0, Repetitions = 1 };
        var dataset = new BenchmarkDataset(options);
        var runId = Guid.NewGuid().ToString("D");
        using var client = Neo4jHarnessQueryClient.CreateClient(endpoint, password);
        await using var target = new Neo4jTarget(client, runId, image);
        await using var observer = new Neo4jHarnessMismatchObserver(endpoint, password, runId, dataset, cancellationToken);
        var report = await new ComparisonRunner(options, observer.Observe)
            .RunAsync([target], SourceRevision, cancellationToken);
        await Neo4jHarnessReportAssertions.VerifyMismatchRunAsync(report, observer);
        await using var reports = new Neo4jHarnessReportFiles();
        await reports.VerifyAsync(report, Neo4jHarnessConstants.MismatchCsvLineCount, cancellationToken);
    }
}
