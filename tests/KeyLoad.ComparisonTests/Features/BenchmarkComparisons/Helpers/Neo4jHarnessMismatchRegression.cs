using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class Neo4jHarnessMismatchRegression
{
    private const string SourceRevision = "comparison-harness-test";

    internal static async Task VerifyAsync(Uri endpoint, string password, string image, CancellationToken cancellationToken)
    {
        var options = Neo4jHarnessConstants.SmallOptions() with { Warmup = 0, Repetitions = 1 };
        var dataset = new BenchmarkDataset(Microsoft.Extensions.Options.Options.Create(options));
        var runId = Guid.NewGuid().ToString("D");
        var failures = new List<Exception>();
        await Neo4jHarnessFailureCollector.AttemptAsync(
            () => VerifyTargetAsync(endpoint, password, image, runId, options, dataset, failures, cancellationToken), failures);
        Neo4jHarnessFailureCollector.ThrowIfAny(Neo4jHarnessConstants.RegressionFailure, failures);
    }

    private static async Task VerifyTargetAsync(Uri endpoint, string password, string image, string runId,
        ComparisonOptions options, BenchmarkDataset dataset, List<Exception> failures, CancellationToken cancellationToken)
    {
        await using var target = CreateTarget(endpoint, password, runId, image);
        await Neo4jHarnessFailureCollector.AttemptAsync(
            () => VerifyObservedAsync(target, endpoint, password, runId, options, dataset, failures, cancellationToken), failures);
    }

    private static Neo4jTarget CreateTarget(Uri endpoint, string password, string runId, string image)
    {
        HttpClient? client = null;
        try
        {
            client = Neo4jHarnessQueryClient.CreateClient(endpoint, password);
            var target = new Neo4jTarget(client, runId, image, NativeExecutionPolicyFixture.Lifecycle(), NativeExecutionPolicyFixture.Read());
            client = null;
            return target;
        }
        finally
        {
            client?.Dispose();
        }
    }

    private static async Task VerifyObservedAsync(Neo4jTarget target, Uri endpoint, string password, string runId,
        ComparisonOptions options, BenchmarkDataset dataset, List<Exception> failures, CancellationToken cancellationToken)
    {
        await using var observer = new Neo4jHarnessMismatchObserver(endpoint, password, runId, dataset, cancellationToken);
        await Neo4jHarnessFailureCollector.AttemptAsync(
            () => VerifyReportsAsync(target, observer, options, failures, cancellationToken), failures);
    }

    private static async Task VerifyReportsAsync(Neo4jTarget target, Neo4jHarnessMismatchObserver observer,
        ComparisonOptions options, List<Exception> failures, CancellationToken cancellationToken)
    {
        await using var reports = new Neo4jHarnessReportFiles();
        await Neo4jHarnessFailureCollector.AttemptAsync(async () =>
        {
            var report = await new ComparisonRunner(Microsoft.Extensions.Options.Options.Create(options), NativeExecutionPolicyFixture.Read(), observer.Observe)
                .RunAsync([target], SourceRevision, cancellationToken);
            await Neo4jHarnessReportAssertions.VerifyMismatchRunAsync(report, observer);
            await reports.VerifyAsync(report, Neo4jHarnessConstants.MismatchCsvLineCount, cancellationToken);
        }, failures);
    }
}
