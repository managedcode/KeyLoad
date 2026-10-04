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
}
