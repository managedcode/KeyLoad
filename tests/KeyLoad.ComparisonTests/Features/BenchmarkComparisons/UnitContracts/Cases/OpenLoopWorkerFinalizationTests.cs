namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class OpenLoopWorkerFinalizationTests
{
    private const string ExpectedOutput = "open-loop failure terminal and original evidence preserved\n";

    [Test]
    public async Task AcScale022AdmittedWorkerFailurePublishesCreateOnlyTerminalAndRetainsOriginalOutput()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await OpenLoopPlanTestLifetime.RunAsync(async (planPath, token) =>
        {
            var root = Path.GetDirectoryName(planPath)!;
            var result = await OpenLoopWorkerFinalizeNodeProcess.RunAsync(root, token).ConfigureAwait(false);
            await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.Error);
            await Assert.That(result.Error).IsEqualTo(string.Empty);
            await Assert.That(result.Output).IsEqualTo(ExpectedOutput);
        }, cancellationToken).ConfigureAwait(false);
    }
}
