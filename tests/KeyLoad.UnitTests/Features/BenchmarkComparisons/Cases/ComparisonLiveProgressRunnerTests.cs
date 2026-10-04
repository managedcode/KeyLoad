using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class ComparisonLiveProgressRunnerTests
{
    private const string InvalidRedisConfiguration = "localhost,ssl=private-progress-canary";
    private const string Canary = "private-progress-canary";
    private const string OracleMarker = "KeyLoadBenchmarkProgress phase=oracle ";
    private const string InitializeMarker = "KeyLoadBenchmarkProgress phase=initialize ";
    private const string CompleteMarker = "KeyLoadBenchmarkProgress phase=complete ";
    private const string UnknownSetup = "completed=0 total=0 failed=0";
    private const string SetupPrefix = "setup:";

    [Test]
    public async Task AcBcLive001NativeSetupFailureRemainsFailedAndOnlyClosedProgressIsEmitted()
    {
        var lines = new List<string>();
        await using var target = new RedisTarget(InvalidRedisConfiguration, Guid.NewGuid().ToString(), string.Empty);
        var report = await new ComparisonRunner(ComparisonHarnessInputs.Small, lines.Add)
            .RunAsync([target], null, TestContext.Current!.Execution.CancellationToken, scenario: Scenario.VectorExact);
        await Assert.That(lines[0].StartsWith(OracleMarker, StringComparison.Ordinal)).IsTrue();
        await Assert.That(lines.Any(line => line.StartsWith(InitializeMarker, StringComparison.Ordinal)
            && line.Contains(UnknownSetup, StringComparison.Ordinal))).IsTrue();
        await Assert.That(lines[^1].StartsWith(CompleteMarker, StringComparison.Ordinal)).IsTrue();
        foreach (var line in lines)
        {
            await ComparisonLiveProgressAssertions.AssertClosedAsync(line);
            await Assert.That(line.Contains(Canary, StringComparison.Ordinal)).IsFalse();
        }
        foreach (var result in report.Cases)
        {
            await Assert.That(result.Status).IsEqualTo(ComparisonStatuses.Failed);
            await Assert.That(result.Detail).StartsWith(SetupPrefix);
            await Assert.That(result.Measurement).IsNull();
            await Assert.That(result.Samples.Length).IsEqualTo(0);
        }
    }

    [Test]
    public async Task AcBcLive004CancellationAtOracleTransitionPreventsTargetInitialization()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken);
        var lines = new List<string>();
        void Observe(string line)
        {
            lines.Add(line);
            if (line.StartsWith(OracleMarker, StringComparison.Ordinal) && line.Contains("total=12 ", StringComparison.Ordinal))
            {
                cancellation.Cancel();
            }
        }
        await using var target = new RedisTarget(InvalidRedisConfiguration, Guid.NewGuid().ToString(), string.Empty);
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => new ComparisonRunner(ComparisonHarnessInputs.Small, Observe)
            .RunAsync([target], null, cancellation.Token, scenario: Scenario.VectorExact));
        await Assert.That(lines.All(line => line.StartsWith(OracleMarker, StringComparison.Ordinal))).IsTrue();
        await Assert.That(lines[^1].Contains("completed=0 total=12 failed=0", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task AcBcLive002RealClosedOutputWriterCannotReplaceNativeSetupOutcome()
    {
        using var file = new ComparisonLiveProgressFile();
        var writer = new StreamWriter(file.Path);
        await writer.DisposeAsync();
        await using var target = new RedisTarget(InvalidRedisConfiguration, Guid.NewGuid().ToString(), string.Empty);
        var report = await new ComparisonRunner(ComparisonHarnessInputs.Small, writer.WriteLine)
            .RunAsync([target], null, TestContext.Current!.Execution.CancellationToken, scenario: Scenario.VectorExact);
        await Assert.That(report.Cases.All(result => result.Status == ComparisonStatuses.Failed
            && result.Detail?.StartsWith(SetupPrefix, StringComparison.Ordinal) == true)).IsTrue();
        await Assert.That(new FileInfo(file.Path).Length).IsEqualTo(0);
    }
}
