namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class IsolatedGitHubFailureDiagnosticsTests
{
    [Test]
    [Arguments("exit")]
    [Arguments("stderr-bound")]
    [Arguments("credential")]
    [Arguments("credential-tail")]
    [Arguments("retention-conflict")]
    public async Task AcIso007FailedChildRetainsBoundedPrivateEvidenceThenJoinsHealthyContinuation(string failure)
    {
        using var directory = new ArtifactTestDirectory();
        try
        {
            var result = await SessionNodeProcess.RunAsync(
                ["--input-type=module", "-e", IsolatedGitHubFailureDiagnosticsProgram.Source,
                    SessionNodeProcess.Module("isolated-github-stream.mjs"), directory.Root, failure],
                TestContext.Current!.Execution.CancellationToken);
            await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.Error);
            await Assert.That(result.Output.Trim()).IsEqualTo("accepted-diagnostic");
            await Assert.That(result.Error).IsEmpty();
        }
        catch (Exception)
        {
            directory.RetainFailureEvidence();
            throw;
        }
    }
}
