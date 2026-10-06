namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class IsolatedAggregateNodeLifetimeTests
{
    private static readonly TimeSpan TestDeadline = TimeSpan.FromSeconds(10);

    [Test]
    public async Task AcImageLife003RealNodeSuccessReturnsExitAndBothExactStreams()
    {
        var result = await RunAsync(IsolatedAggregateNodeLifetimeProgram.Success);

        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.Output).IsEqualTo(IsolatedAggregateNodeLifetimeProgram.StandardOutput);
        await Assert.That(result.Error).IsEqualTo(IsolatedAggregateNodeLifetimeProgram.StandardError);
    }

    [Test]
    public async Task AcImageLife003RealNodeNonzeroExitRetainsBothStreams()
    {
        var result = await RunAsync(IsolatedAggregateNodeLifetimeProgram.NonzeroExit);

        await Assert.That(result.ExitCode).IsEqualTo(IsolatedAggregateNodeLifetimeProgram.NonzeroExitCode);
        await Assert.That(result.Output).IsEqualTo(IsolatedAggregateNodeLifetimeProgram.NonzeroOutput);
        await Assert.That(result.Error).IsEqualTo(IsolatedAggregateNodeLifetimeProgram.NonzeroError);
    }

    [Test]
    public async Task AcImageLife003RealNodeSingleOutputLimitRemainsVisible()
    {
        var failure = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => RunAsync(IsolatedAggregateNodeLifetimeProgram.StandardOutputLimit))
            ?? throw new InvalidOperationException(IsolatedAggregateNodeLifetimeProgram.OutputLimitMessage);

        await Assert.That(failure.Message).IsEqualTo(IsolatedAggregateNodeLifetimeProgram.OutputLimitMessage);
    }

    [Test]
    public async Task AcImageLife003RealNodeRetainsBothIndependentPipeLimitFailures()
    {
        using var timeout = new CancellationTokenSource(TestDeadline, TimeProvider.System);
        using var prompt = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken, timeout.Token);
        var captured = await IsolatedAggregateNodePipeFailureFixture.CaptureBothAsync(
            prompt.Token);
        var outputFailures = captured.Combined.InnerExceptions
            .Where(error => error is InvalidOperationException
                && error.Message == IsolatedAggregateNodeLifetimeProgram.OutputLimitMessage)
            .ToArray();

        await Assert.That(outputFailures.Length).IsEqualTo(IsolatedAggregateNodeLifetimeProgram.OutputFailureCount);
        await Assert.That(ReferenceEquals(outputFailures[0], outputFailures[1])).IsFalse();
        await Assert.That(ReferenceEquals(outputFailures[0], captured.StandardOutput)).IsTrue();
        await Assert.That(ReferenceEquals(outputFailures[1], captured.StandardError)).IsTrue();
    }

    [Test]
    public async Task AcImageLife003CancellationKillsAndReapsRealNodeProcessTree()
        => await IsolatedAggregateNodeLifetimeTestSupport.CancelOwnedTreeAsync(TestDeadline);

    private static async Task<IsolatedAggregateNodeResult> RunAsync(string scenario)
    {
        using var timeout = new CancellationTokenSource(TestDeadline, TimeProvider.System);
        using var prompt = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken, timeout.Token);
        return await IsolatedAggregateNodeProcess.RunAsync(Arguments(scenario), prompt.Token);
    }

    private static string[] Arguments(string scenario)
        => ["-e", IsolatedAggregateNodeLifetimeProgram.Source, scenario];

}
