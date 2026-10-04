namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>AC-ISO-006/007: controlled parser inputs, never authenticated provider evidence.</summary>
internal sealed class IsolatedGitHubMetadataTests
{
    [Test]
    [Arguments("valid")]
    [Arguments("valid-legacy-url")]
    [Arguments("valid-native-no-attempt")]
    [Arguments("source")]
    [Arguments("run")]
    [Arguments("attempt")]
    [Arguments("repository")]
    [Arguments("workflow")]
    [Arguments("branch")]
    [Arguments("duplicate-job")]
    [Arguments("duplicate-artifact")]
    [Arguments("pagination-count")]
    [Arguments("missing-job")]
    [Arguments("missing-artifact")]
    [Arguments("foreign-job")]
    [Arguments("failed-job")]
    [Arguments("missing-step")]
    [Arguments("duplicate-step")]
    [Arguments("failed-step")]
    [Arguments("expired-artifact")]
    [Arguments("artifact-source")]
    [Arguments("artifact-run")]
    [Arguments("artifact-repository")]
    [Arguments("artifact-time")]
    [Arguments("artifact-hash")]
    [Arguments("duplicate-json")]
    public async Task AcIso007StrictMetadataProjectionRejectsForeignOrPartialEvidence(string corruption)
    {
        var result = await IsolatedAggregateNodeProcess.RunAsync(
            ["--input-type=module", "-e", IsolatedGitHubNodeProgram.Metadata,
                IsolatedAggregateNodeProcess.Module("isolated-github-validation.mjs"), corruption],
            TestContext.Current!.Execution.CancellationToken);
        var accepted = corruption.StartsWith("valid", StringComparison.Ordinal);
        await Assert.That(result.ExitCode).IsEqualTo(accepted ? 0 : 1);
        await Assert.That(result.Output.Trim()).IsEqualTo(accepted ? "accepted" : "rejected");
        await Assert.That(result.Error).IsEmpty();
    }
}
