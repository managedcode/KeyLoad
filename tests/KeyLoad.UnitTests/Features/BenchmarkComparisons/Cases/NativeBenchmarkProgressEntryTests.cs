namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class NativeBenchmarkProgressEntryTests
{
    private const string Module = "run-workload.mjs";
    private const string NodeEval = "--eval";
    private const string ModuleMode = "--input-type=module";
    private const string Accepted = "accepted:";

    // AC-BC-LIVE-002/003: real child output, original exit, cancellation and confined file reads.
    [Test]
    [Arguments("live-exit")]
    [Arguments("cancel")]
    [Arguments("invalid")]
    [Arguments("links-bounds")]
    [Arguments("no-cell")]
    public async Task AcBcLiveEntryPreservesNativeProcessOutcomeAndOnlyRelaysBoundedProgress(string scenario)
    {
        using var directory = new ImageBundleTestDirectory();
        var result = await IsolatedAggregateNodeProcess.RunAsync(
            [ModuleMode, NodeEval, NativeBenchmarkProgressNodeProgram.Source, IsolatedAggregateNodeProcess.Module(Module), scenario, directory.Root],
            TestContext.Current!.Execution.CancellationToken);
        await Assert.That(result.Error).IsEmpty();
        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.Output).Contains(Accepted + scenario);
        if (scenario == "live-exit")
        {
            await Assert.That(result.Output).Contains(NativeBenchmarkProgressNodeProgram.MeasureLine);
            await Assert.That(result.Output).Contains(NativeBenchmarkProgressNodeProgram.CompleteLine);
            await Assert.That(result.Output.IndexOf(NativeBenchmarkProgressNodeProgram.MeasureLine, StringComparison.Ordinal))
                .IsLessThan(result.Output.IndexOf(Accepted, StringComparison.Ordinal));
        }
        await Assert.That(result.Output).DoesNotContain(NativeBenchmarkProgressNodeProgram.PrivateCanary);
    }
}
