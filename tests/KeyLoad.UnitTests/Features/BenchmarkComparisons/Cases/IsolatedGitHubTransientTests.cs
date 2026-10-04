namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class IsolatedGitHubTransientTests
{
    [Test]
    [Arguments("original", true)]
    [Arguments("internal-error", true)]
    [Arguments("bad-gateway", true)]
    [Arguments("gateway-timeout", true)]
    [Arguments("second-retry", true)]
    [Arguments("third-retry", true)]
    [Arguments("retry-after", true)]
    [Arguments("retry-date", true)]
    [Arguments("provider-limits", true)]
    [Arguments("unauthorized", false)]
    [Arguments("forbidden", false)]
    [Arguments("not-found", false)]
    [Arguments("unsupported-status", false)]
    [Arguments("successful-response", false)]
    [Arguments("invalid-retry", false)]
    [Arguments("invalid-reset", false)]
    [Arguments("duplicate-header", false)]
    [Arguments("missing-headers", false)]
    [Arguments("array-headers", false)]
    [Arguments("wait-budget", false)]
    [Arguments("repeat-budget", false)]
    [Arguments("negative-wait", false)]
    [Arguments("negative-repeat", false)]
    [Arguments("fractional-repeat", false)]
    public async Task AcBcFail005RetriesOriginalTransientGetFailuresWithinBudgets(string input, bool accepted)
    {
        var module = IsolatedAggregateNodeProcess.Module("isolated-github-rate.mjs");
        var original = Path.Combine(IsolatedAggregateNodeProcess.RepositoryRoot(), "tests", "KeyLoad.UnitTests",
            "Features", "BenchmarkComparisons", "IsolatedGitHubTransientOriginal.headers");
        var result = await IsolatedAggregateNodeProcess.RunAsync(
            ["--input-type=module", "-e", IsolatedGitHubTransientProgram.Source, module, input, original],
            TestContext.Current!.Execution.CancellationToken);
        await Assert.That(result.ExitCode).IsEqualTo(accepted ? 0 : 1);
        await Assert.That(result.Output.Trim()).IsEqualTo(accepted ? "accepted" : "rejected");
        await Assert.That(result.Error).IsEmpty();
    }
}
