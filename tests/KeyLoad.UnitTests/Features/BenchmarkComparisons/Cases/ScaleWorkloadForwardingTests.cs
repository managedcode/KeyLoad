namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class ScaleWorkloadForwardingTests
{
    private const string ModuleName = "run-workload.mjs";

    [Test]
    [Arguments("control")]
    [Arguments("scale")]
    [Arguments("invalid")]
    public async Task AcScale014RunWorkloadPinsProfileArgumentAndTimeout(string scenario)
    {
        var response = await IsolatedAggregateNodeProcess.RunAsync(
            ["--input-type=module", "-e", ScaleWorkloadForwardingNodeProgram.Source,
                IsolatedAggregateNodeProcess.Module(ModuleName), scenario],
            TestContext.Current!.Execution.CancellationToken);
        await Assert.That(response.ExitCode).IsEqualTo(0);
        await Assert.That(response.Error).IsEqualTo(string.Empty);
    }
}
