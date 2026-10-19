namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class IsolatedGitHubFileTests
{
    [Test]
    [Arguments("valid")]
    [Arguments("hash")]
    [Arguments("size")]
    [Arguments("symlink")]
    [Arguments("existing")]
    [Arguments("bound")]
    [Arguments("timeout")]
    [Arguments("duplicate-entry")]
    [Arguments("missing-entry")]
    [Arguments("unsafe-entry")]
    public async Task AcIso007RealFileStreamAndInventoryRejectChangedOrUnsafeEvidence(string corruption)
    {
        using var directory = new ArtifactTestDirectory();
        var result = await SessionNodeProcess.RunAsync(
            ["--input-type=module", "-e", IsolatedGitHubFileProgram.Source,
                SessionNodeProcess.Module("isolated-github-stream.mjs"), directory.Root, corruption],
            TestContext.Current!.Execution.CancellationToken);
        await Assert.That(result.ExitCode).IsEqualTo(corruption == "valid" ? 0 : 1);
        await Assert.That(result.Output.Trim()).IsEqualTo(corruption == "valid" ? "accepted" : "rejected");
        await Assert.That(result.Error).IsEmpty();
    }

    [Test]
    [Arguments(219259, "async")]
    [Arguments(219259, "sync")]
    [Arguments(1048576, "async")]
    [Arguments(1048576, "sync")]
    [Arguments(8388608, "async")]
    [Arguments(8388608, "sync")]
    public async Task AcIso007LargeStreamRetainsOriginalBytesRefusesOneOverAndJoinsHealthyContinuation(
        int length, string writer)
    {
        using var directory = new ArtifactTestDirectory();
        try
        {
            var result = await SessionNodeProcess.RunAsync(
                ["--input-type=module", "-e", IsolatedGitHubFileProgram.LargeSource,
                    SessionNodeProcess.Module("isolated-github-stream.mjs"), directory.Root,
                    length.ToString(System.Globalization.CultureInfo.InvariantCulture), writer],
                TestContext.Current!.Execution.CancellationToken);
            await Assert.That(result.ExitCode).IsEqualTo(0);
            await Assert.That(result.Output.Trim()).IsEqualTo("accepted-large");
            await Assert.That(result.Error).IsEmpty();
        }
        catch (Exception)
        {
            directory.RetainFailureEvidence();
            throw;
        }
    }
}
