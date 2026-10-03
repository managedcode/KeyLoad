namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class IsolatedGitHubCompleteTests
{
    [Test]
    [Arguments("valid")]
    [Arguments("missing-cell-job")]
    [Arguments("missing-cell-artifact")]
    [Arguments("duplicate-case-name")]
    [Arguments("unknown-case")]
    [Arguments("unknown-worker")]
    [Arguments("image-job-failed")]
    [Arguments("image-step-missing")]
    [Arguments("image-artifact-expired")]
    [Arguments("zip-total")]
    public async Task AcIso006ControlledClosed270MetadataRequiresEveryCellAndOneSuccessfulImageJob(string corruption)
    {
        var result = await IsolatedAggregateNodeProcess.RunAsync(
            ["--input-type=module", "-e", IsolatedGitHubCompleteProgram.Source,
                IsolatedAggregateNodeProcess.Module("isolated-github-selection.mjs"), corruption],
            TestContext.Current!.Execution.CancellationToken);
        await Assert.That(result.ExitCode).IsEqualTo(corruption == "valid" ? 0 : 1);
        await Assert.That(result.Output.Trim()).IsEqualTo(corruption == "valid" ? "accepted" : "rejected");
        await Assert.That(result.Error).IsEmpty();
    }
}
