using System.Net;
using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;

namespace KeyLoad.UnitTests;

internal sealed class ComparisonHarnessTests
{
    private static ComparisonOptions Small => new()
    {
        Documents = 16,
        Operations = 12,
        Warmup = 2,
        Repetitions = 2,
        Concurrency = 2,
        Dimensions = 8,
        TopK = 3,
        PayloadBytes = 128
    };

    [Test]
    public async Task Neo4jQueryErrorsFailEvenWhenHttpStatusIsAccepted()
    {
        using var handler = new QueryFailureHandler();
        using var http = new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("http://localhost/") };
        await using var target = new Neo4jTarget(http, Guid.NewGuid().ToString("N"), "test-image");
        var error = await Assert.ThrowsExactlyAsync<ComparisonFailureException>(() => target.InitializeAsync(new BenchmarkDataset(Small), TestContext.Current!.Execution.CancellationToken));
        await Assert.That(error!.Message).IsEqualTo("Neo4j:Neo.ClientError.Statement.SyntaxError");
        await Assert.That(error!.Message).DoesNotContain("sensitive-query");
    }

    private sealed class QueryFailureHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted) { Content = new StringContent("""
                {"errors":[{"code":"Neo.ClientError.Statement.SyntaxError","message":"sensitive-query"}]}
                """) });
    }

    [Test]
    public async Task RunnerRejectsIncorrectPayloadAndPreservesUnsupportedAndSetupFailures()
    {
        var options = Small with { Warmup = 0, Repetitions = 1 };
        await using var correct = new ReadTarget("correct");
        await using var incorrect = new ReadTarget("incorrect", wrongPayload: true);
        await using var setupFailed = new ReadTarget("setup-failed", failSetup: true);
        IComparisonTarget[] targets = [correct, incorrect, setupFailed];
        var report = await new ComparisonRunner(options).RunAsync(targets, null, TestContext.Current!.Execution.CancellationToken);
        var good = await Assert.That(report.Cases).HasSingleItem(item => item.Target == "correct" && item.Scenario == Scenario.PointRead);
        await Assert.That(good.Measurement!.Successes).IsEqualTo(options.Operations);
        var bad = await Assert.That(report.Cases).HasSingleItem(item => item.Target == "incorrect" && item.Scenario == Scenario.PointRead);
        await Assert.That(bad.Status).IsEqualTo("failed");
        await Assert.That(bad.Measurement!.UsefulOperationsPerSecond).IsEqualTo(0);
        foreach (var sample in bad.Samples)
        {
            await Assert.That(sample.Error).IsEqualTo("PointReadMismatch");
        }
        await Assert.That(report.Cases.Where(item => item.Status == "unsupported")).All(item => item.Measurement == null);
        await Assert.That(System.Linq.Enumerable.Single(report.Cases, item => item.Target == "setup-failed" && item.Scenario == Scenario.PointRead).Status).IsEqualTo("failed");
        await Assert.That(ReportWriter.Markdown(report)).DoesNotContain("secret-value");
    }

    [Test]
    public async Task WarmupExcludedRepetitionsRetainedAndCsvContainsEveryAttempt()
    {
        await using var target = new ReadTarget("correct");
        var report = await new ComparisonRunner(Small).RunAsync([target], "test-revision", TestContext.Current!.Execution.CancellationToken);
        await Assert.That(report.Cases.Count(item => item.Status == "measured")).IsEqualTo(Small.Repetitions);
        foreach (var item in report.Cases.Where(item => item.Measurement is not null))
        {
            await Assert.That(item.Samples.Length).IsEqualTo(Small.Operations);
        }
        await Assert.That(target.Executions).IsEqualTo(Small.Repetitions * (Small.Operations + Small.Warmup));
        var output = Path.Combine(Path.GetTempPath(), "keyload-report-" + Guid.NewGuid().ToString("N"));
        try
        {
            await ReportWriter.WriteAsync(report, output, TestContext.Current!.Execution.CancellationToken);
            var lines = await File.ReadAllLinesAsync(Path.Combine(output, "samples.csv"));
            await Assert.That(lines.Length).IsEqualTo(1 + Small.Operations * Small.Repetitions);
            var markdown = await File.ReadAllTextAsync(Path.Combine(output, "results.md"));
            await Assert.That(markdown).Contains("not an equal-durability");
        }
        finally
        {
            if (Directory.Exists(output))
            {
                Directory.Delete(output, true);
            }
        }
    }

    private sealed class ReadTarget(string name, bool wrongPayload = false, bool failSetup = false) : IComparisonTarget, IComparisonSession
    {
        private int executions;
        public int Executions => executions;
        public TargetProfile Profile => new(name, "test", "unit fixture", "none", "static", "in-process", "none", null);
        public bool Supports(Scenario scenario) => scenario == Scenario.PointRead;
        public Task InitializeAsync(IComparisonCorpus dataset, CancellationToken cancellationToken)
            => failSetup ? Task.FromException(new InvalidOperationException("secret-value")) : Task.CompletedTask;
        public Task<IComparisonSession> OpenSessionAsync(CancellationToken cancellationToken) => Task.FromResult<IComparisonSession>(this);
        public Task<FoundDocument?> ReadAsync(BenchmarkDocument document, CancellationToken cancellationToken)
            => Task.FromResult<FoundDocument?>(new(document.Id, wrongPayload ? "{}" : document.Json));
        public async Task<KeyLoad.Comparisons.OperationResult> ExecuteAsync(Scenario scenario, BenchmarkDocument document, CancellationToken cancellationToken)
        { Interlocked.Increment(ref executions); return new(Document: await ReadAsync(document, cancellationToken)); }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
