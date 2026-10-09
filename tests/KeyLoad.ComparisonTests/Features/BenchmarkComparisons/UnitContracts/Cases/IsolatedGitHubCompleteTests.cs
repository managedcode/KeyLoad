namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class IsolatedGitHubCompleteTests
{
    private const string LegacyJobNames = "legacy";

    [Test]
    [Arguments("valid")]
    [Arguments(LegacyJobNames)]
    [Arguments("modern")]
    [Arguments("modern-failed")]
    [Arguments("modern-duplicate")]
    [Arguments("modern-foreign-name")]
    [Arguments("modern-preflight-replaces-worker")]
    [Arguments("modern-mixed")]
    [Arguments("missing-cell-job")]
    [Arguments("missing-cell-artifact")]
    [Arguments("duplicate-case-name")]
    [Arguments("unknown-case")]
    [Arguments("unknown-worker")]
    [Arguments("image-job-failed")]
    [Arguments("image-step-missing")]
    [Arguments("image-artifact-expired")]
    [Arguments("zip-total")]
    public async Task AcIso006ControlledClosed220MetadataRequiresEveryCellAndOneSuccessfulImageJob(string corruption)
    {
        var result = await IsolatedAggregateNodeProcess.RunAsync(
            ["--input-type=module", "-e", IsolatedGitHubCompleteProgram.Source,
                IsolatedAggregateNodeProcess.Module("isolated-github-selection.mjs"), corruption],
            TestContext.Current!.Execution.CancellationToken);
        var valid = corruption is "valid" or "modern" or "modern-failed";
        await Assert.That(result.ExitCode).IsEqualTo(valid ? 0 : 1);
        await Assert.That(result.Output.Trim()).IsEqualTo(valid ? "accepted" : "rejected");
        await Assert.That(result.Error).IsEmpty();
    }
}
