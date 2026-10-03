namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class ImageHttpDeadlineTests
{
    private const string AbortScenario = "abort";
    private const string SuccessScenario = "success";
    private const string SyncFailureScenario = "sync-failure";
    private const string AsyncFailureScenario = "async-failure";
    private const string LateAbortScenario = "late-abort";
    private const string InvalidScenario = "invalid";
    private const int PromptTimeoutSeconds = 10;
    private const string ExpectedErrorOutput = "";

    [Test]
    public async Task AcImageLife001TimeoutAbortsTheOriginalOperationAndExitsNormally()
        => await AssertScenarioAsync(AbortScenario, "aborted");

    [Test]
    public async Task AcImageLife001SuccessfulOperationClearsTheReferencedLongTimer()
        => await AssertScenarioAsync(SuccessScenario, "success:42");

    [Test]
    public async Task AcImageLife001SynchronousFailurePreservesTheOriginalError()
        => await AssertScenarioAsync(SyncFailureScenario, "sync-error-preserved");

    [Test]
    public async Task AcImageLife001AsynchronousFailurePreservesTheOriginalError()
        => await AssertScenarioAsync(AsyncFailureScenario, "async-error-preserved");

    [Test]
    public async Task AcImageLife003SuccessfulCleanupCannotAbortTheSignalLater()
        => await AssertScenarioAsync(LateAbortScenario, "no-late-abort");

    [Test]
    public async Task AcImageLife003InvalidBoundsRejectBeforeCallingTheOperation()
        => await AssertScenarioAsync(InvalidScenario, "invalid-bounds-rejected");

    private static async Task AssertScenarioAsync(string scenario, string expectedOutput)
    {
        using var prompt = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current!.Execution.CancellationToken);
        prompt.CancelAfter(TimeSpan.FromSeconds(PromptTimeoutSeconds));
        var result = await IsolatedAggregateNodeProcess.RunAsync(
            ["--input-type=module", "-e", ImageHttpDeadlineNodeProgram.Source,
                IsolatedAggregateNodeProcess.Module("image-manifest.mjs"), scenario], prompt.Token);
        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.Output.Trim()).IsEqualTo(expectedOutput);
        await Assert.That(result.Error).IsEqualTo(ExpectedErrorOutput);
    }
}
