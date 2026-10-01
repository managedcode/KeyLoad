using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;
using System.Net;

namespace KeyLoad.UnitTests;

public sealed class ComparisonHarnessTests
{
    private static ComparisonOptions Small => new() { Documents = 16, Operations = 12, Warmup = 2, Repetitions = 2,
        Concurrency = 2, Dimensions = 8, TopK = 3, PayloadBytes = 128 };

    [Fact]
    public void CorpusIsByteExactAndReproducibleAcrossTargetsAndSeeds()
    {
        var a = new BenchmarkDataset(Small); var b = new BenchmarkDataset(Small);
        Assert.Equal(a.Sha256, b.Sha256);
        Assert.NotEqual(a.Sha256, new BenchmarkDataset(Small with { Seed = Small.Seed + 1 }).Sha256);
        Assert.All(a.Documents, document => Assert.Equal(Small.PayloadBytes, System.Text.Encoding.UTF8.GetByteCount(document.Json)));
        Assert.All(a.Documents, document => Assert.Equal(document.Id, a.ExactNeighbors(document)[0].Id));
        Assert.Equal(a.Documents[0].Json, b.Documents[0].Json);
        Assert.True(BenchmarkDataset.SameJson("{\"id\":1,\"payload\":\"x\"}", "{\"payload\":\"x\",\"id\":1}"));
        Assert.False(BenchmarkDataset.SameJson("{\"id\":1}", "{\"id\":2}"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BenchmarkDataset(Small with { TopK = 17 }));
    }

    [Fact]
    public void FailedAttemptsAndTimeoutLatencyRemainVisibleAndDoNotInflateUsefulThroughput()
    {
        OperationSample[] samples = [new(0, 0, 0, 1, true, null, 128, "m1", new(0.2, 0.5, 0.3)),
            new(1, 0, 1, 101, false, "TimeoutException", 128, null, null),
            new(2, 0, 101, 103, true, null, 128, "m2", new(0.5, 1, 0.5))];
        var result = ComparisonRunner.Summarize(samples, 2);
        Assert.Equal(3, result.Attempts); Assert.Equal(2, result.Successes); Assert.Equal(1, result.Failures);
        Assert.Equal(1, result.UsefulOperationsPerSecond); Assert.Equal(100, result.Latency.P99Ms);
        Assert.Equal(2, result.UniqueCompletedMessages); Assert.NotNull(result.Enqueue);
    }

    [Fact]
    public void GraphOracleHandlesCyclesDepthAndDisconnectedComponents()
    {
        var data = new BenchmarkDataset(Small);
        Assert.Equal(data.Documents[1..4].Select(document => document.Id), data.Reachable(data.Documents[0], 1));
        Assert.Equal(data.Documents[1..7].Select(document => document.Id), data.Reachable(data.Documents[0], 2));
        Assert.Equal(data.Documents[1..8].Select(document => document.Id), data.Reachable(data.Documents[0], 5));
        Assert.DoesNotContain(data.Reachable(data.Documents[0], 5), id => id == data.Documents[8].Id);
        Assert.NotEqual(data.Sha256, new BenchmarkDataset(Small with { GraphFanOut = 2 }).Sha256);
        Assert.Empty(new BenchmarkDataset(Small with { Documents = 1, TopK = 1 }).Edges);
    }

    [Fact]
    public async Task Neo4jQueryErrorsFailEvenWhenHttpStatusIsAccepted()
    {
        var http = new HttpClient(new QueryFailureHandler()) { BaseAddress = new Uri("http://localhost/") };
        var target = new Neo4jTarget(http, Guid.NewGuid().ToString("N"), "test-image");
        var error = await Assert.ThrowsAsync<ComparisonFailure>(() => target.InitializeAsync(new BenchmarkDataset(Small), TestContext.Current.CancellationToken));
        Assert.Equal("Neo4j:Neo.ClientError.Statement.SyntaxError", error.Message);
        Assert.DoesNotContain("sensitive-query", error.Message);
        http.Dispose();
    }

    private sealed class QueryFailureHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted) { Content = new StringContent("""
                {"errors":[{"code":"Neo.ClientError.Statement.SyntaxError","message":"sensitive-query"}]}
                """) });
    }

    [Fact]
    public async Task RunnerRejectsIncorrectPayloadAndPreservesUnsupportedAndSetupFailures()
    {
        var options = Small with { Warmup = 0, Repetitions = 1 };
        IComparisonTarget[] targets = [new ReadTarget("correct"), new ReadTarget("incorrect", wrongPayload: true),
            new ReadTarget("setup-failed", failSetup: true)];
        var report = await new ComparisonRunner(options).RunAsync(targets, null, TestContext.Current.CancellationToken);
        var good = Assert.Single(report.Cases, item => item.Target == "correct" && item.Scenario == Scenario.PointRead);
        Assert.Equal(options.Operations, good.Measurement!.Successes);
        var bad = Assert.Single(report.Cases, item => item.Target == "incorrect" && item.Scenario == Scenario.PointRead);
        Assert.Equal("failed", bad.Status); Assert.Equal(0, bad.Measurement!.UsefulOperationsPerSecond);
        Assert.All(bad.Samples, sample => Assert.Equal("PointReadMismatch", sample.Error));
        Assert.All(report.Cases.Where(item => item.Status == "unsupported"), item => Assert.Null(item.Measurement));
        Assert.Equal("failed", Assert.Single(report.Cases, item => item.Target == "setup-failed" && item.Scenario == Scenario.PointRead).Status);
        Assert.DoesNotContain("secret-value", ReportWriter.Markdown(report));
    }

    [Fact]
    public async Task WarmupExcludedRepetitionsRetainedAndCsvContainsEveryAttempt()
    {
        var target = new ReadTarget("correct");
        var report = await new ComparisonRunner(Small).RunAsync([target], "test-revision", TestContext.Current.CancellationToken);
        Assert.Equal(Small.Repetitions, report.Cases.Count(item => item.Status == "measured"));
        Assert.All(report.Cases.Where(item => item.Measurement is not null), item => Assert.Equal(Small.Operations, item.Samples.Length));
        Assert.Equal(Small.Repetitions * (Small.Operations + Small.Warmup), target.Executions);
        var output = Path.Combine(Path.GetTempPath(), "keyload-report-" + Guid.NewGuid().ToString("N"));
        try
        {
            await ReportWriter.WriteAsync(report, output, TestContext.Current.CancellationToken);
            Assert.Equal(1 + Small.Operations * Small.Repetitions, File.ReadAllLines(Path.Combine(output, "samples.csv")).Length);
            Assert.Contains("not an equal-durability", File.ReadAllText(Path.Combine(output, "results.md")));
        }
        finally { if (Directory.Exists(output)) Directory.Delete(output, true); }
    }

    private sealed class ReadTarget(string name, bool wrongPayload = false, bool failSetup = false) : IComparisonTarget, IComparisonSession
    {
        private int executions;
        public int Executions => executions;
        public TargetProfile Profile => new(name, "test", "unit fixture", "none", "static", "in-process", "none", null);
        public bool Supports(Scenario scenario) => scenario == Scenario.PointRead;
        public Task InitializeAsync(BenchmarkDataset dataset, CancellationToken cancellationToken)
            => failSetup ? Task.FromException(new InvalidOperationException("secret-value")) : Task.CompletedTask;
        public Task<IComparisonSession> OpenSessionAsync(CancellationToken cancellationToken) => Task.FromResult<IComparisonSession>(this);
        public Task<FoundDocument?> ReadAsync(BenchmarkDocument document, CancellationToken cancellationToken)
            => Task.FromResult<FoundDocument?>(new(document.Id, wrongPayload ? "{}" : document.Json));
        public async Task<KeyLoad.Comparisons.OperationResult> ExecuteAsync(Scenario scenario, BenchmarkDocument document, CancellationToken cancellationToken)
        { Interlocked.Increment(ref executions); return new(Document: await ReadAsync(document, cancellationToken)); }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
