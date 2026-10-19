namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

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
    public async Task AcIso007HeaderParserBoundsOnlyPermittedProviderWaits(string input)
    {
        var result = await SessionNodeProcess.RunAsync(
            ["--input-type=module", "-e", IsolatedGitHubRateProgram.Source,
                SessionNodeProcess.Module("isolated-github-rate.mjs"), input],
            TestContext.Current!.Execution.CancellationToken);
        var accepted = input is "reset" or "retry-after" or "both";
        await Assert.That(result.ExitCode).IsEqualTo(accepted ? 0 : 1);
        await Assert.That(result.Output.Trim()).IsEqualTo(accepted ? "accepted" : "rejected");
        await Assert.That(result.Error).IsEmpty();
    }

    [Test]
    [Arguments("execution-recorded-cancel")]
    [Arguments("execution-recorded-502")]
    [Arguments("execution-recorded-success")]
    [Arguments("execution-exit-success")]
    [Arguments("execution-partial-body")]
    [Arguments("execution-recorded-partial")]
    [Arguments("execution-negative-body")]
    [Arguments("execution-oversized-body")]
    [Arguments("execution-unsafe-body")]
    [Arguments("execution-wrong-error")]
    [Arguments("execution-wrong-hash")]
    [Arguments("execution-auth")]
    [Arguments("execution-complete-invalid-json")]
    [Arguments("execution-timeout")]
    [Arguments("execution-overflow")]
    [Arguments("execution-spawn-failure")]
    [Arguments("execution-cancelled")]
    [Arguments("execution-signal")]
    [Arguments("execution-repeat-budget")]
    [Arguments("execution-wait-budget")]
    public async Task AcIso007OriginalStreamFailureRetriesOnlyWithinUnchangedMetadataAndWaitBudgets(string input)
    {
        var result = await SessionNodeProcess.RunAsync(
            ["--input-type=module", "-e", IsolatedGitHubRateProgram.Source,
                SessionNodeProcess.Module("isolated-github-rate.mjs"), input],
            TestContext.Current!.Execution.CancellationToken);
        var accepted = input is "execution-recorded-cancel" or "execution-recorded-502" or "execution-recorded-success"
            or "execution-partial-body" or "execution-recorded-partial";
        await Assert.That(result.ExitCode).IsEqualTo(accepted ? 0 : 1);
        await Assert.That(result.Output.Trim()).IsEqualTo(accepted ? "accepted" : "rejected");
        await Assert.That(result.Error).IsEmpty();
    }
}
