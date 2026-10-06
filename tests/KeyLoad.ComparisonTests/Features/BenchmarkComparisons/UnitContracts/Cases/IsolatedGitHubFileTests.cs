namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

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
    public async Task AcIso007RealNativeFileStreamAndInventoryRejectChangedOrUnsafeEvidence(string corruption)
    {
        using var directory = new ImageBundleTestDirectory();
        var result = await IsolatedAggregateNodeProcess.RunAsync(
            ["--input-type=module", "-e", IsolatedGitHubFileProgram.Source,
                IsolatedAggregateNodeProcess.Module("isolated-github-stream.mjs"), directory.Root, corruption],
            TestContext.Current!.Execution.CancellationToken);
        await Assert.That(result.ExitCode).IsEqualTo(corruption == "valid" ? 0 : 1);
        await Assert.That(result.Output.Trim()).IsEqualTo(corruption == "valid" ? "accepted" : "rejected");
        await Assert.That(result.Error).IsEmpty();
    }
}
