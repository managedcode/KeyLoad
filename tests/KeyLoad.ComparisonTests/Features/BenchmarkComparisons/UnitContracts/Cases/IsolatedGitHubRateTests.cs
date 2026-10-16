namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class IsolatedGitHubRateTests
{
    [Test]
    [Arguments("reset")]
    [Arguments("retry-after")]
    [Arguments("both")]
    [Arguments("unauthorized")]
    [Arguments("absent")]
    [Arguments("remaining")]
    [Arguments("invalid-reset")]
    [Arguments("invalid-retry")]
    [Arguments("duplicate-header")]
    [Arguments("wait-budget")]
    [Arguments("repeat-budget")]
    public async Task AcIso007NativeHeaderParserBoundsOnlyPermittedProviderWaits(string input)
    {
        var result = await IsolatedAggregateNodeProcess.RunAsync(
            ["--input-type=module", "-e", IsolatedGitHubRateProgram.Source,
                IsolatedAggregateNodeProcess.Module("isolated-github-rate.mjs"), input],
            TestContext.Current!.Execution.CancellationToken);
        var accepted = input is "reset" or "retry-after" or "both";
        await Assert.That(result.ExitCode).IsEqualTo(accepted ? 0 : 1);
        await Assert.That(result.Output.Trim()).IsEqualTo(accepted ? "accepted" : "rejected");
        await Assert.That(result.Error).IsEmpty();
    }

    [Test]
    [Arguments("native-recorded-cancel")]
    [Arguments("native-recorded-502")]
    [Arguments("native-recorded-success")]
    [Arguments("native-exit-success")]
    [Arguments("native-partial-body")]
    [Arguments("native-wrong-error")]
    [Arguments("native-wrong-hash")]
    [Arguments("native-auth")]
    [Arguments("native-complete-invalid-json")]
    [Arguments("native-timeout")]
    [Arguments("native-overflow")]
    [Arguments("native-spawn-failure")]
    [Arguments("native-cancelled")]
    [Arguments("native-signal")]
    [Arguments("native-repeat-budget")]
    [Arguments("native-wait-budget")]
    public async Task AcIso007OriginalNativeStreamFailureRetriesOnlyWithinUnchangedMetadataAndWaitBudgets(string input)
    {
        var result = await IsolatedAggregateNodeProcess.RunAsync(
            ["--input-type=module", "-e", IsolatedGitHubRateProgram.Source,
                IsolatedAggregateNodeProcess.Module("isolated-github-rate.mjs"), input],
            TestContext.Current!.Execution.CancellationToken);
        var accepted = input is "native-recorded-cancel" or "native-recorded-502" or "native-recorded-success";
        await Assert.That(result.ExitCode).IsEqualTo(accepted ? 0 : 1);
        await Assert.That(result.Output.Trim()).IsEqualTo(accepted ? "accepted" : "rejected");
        await Assert.That(result.Error).IsEmpty();
    }
}
