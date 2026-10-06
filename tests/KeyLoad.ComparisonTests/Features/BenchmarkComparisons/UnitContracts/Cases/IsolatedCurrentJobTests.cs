namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>AC-NGR-001 policy cases from the retained original provider job record; these do not authenticate GitHub.</summary>
internal sealed class IsolatedCurrentJobTests
{
    [Test]
    [Arguments("queued", "queued", true)]
    [Arguments("running", "running", true)]
    [Arguments("legacy-url", "queued", true)]
    [Arguments("missing-attempt", "queued", true)]
    [Arguments("legacy-running", "running", true)]
    [Arguments("missing-attempt-running", "running", true)]
    [Arguments("wrong-id", "rejected", false)]
    [Arguments("wrong-run", "rejected", false)]
    [Arguments("wrong-attempt", "rejected", false)]
    [Arguments("wrong-source", "rejected", false)]
    [Arguments("wrong-name", "rejected", false)]
    [Arguments("wrong-url", "rejected", false)]
    [Arguments("wrong-workflow", "rejected", false)]
    [Arguments("wrong-branch", "rejected", false)]
    [Arguments("terminal", "rejected", false)]
    [Arguments("unknown-state", "rejected", false)]
    [Arguments("queued-conclusion", "rejected", false)]
    [Arguments("running-conclusion", "rejected", false)]
    [Arguments("malformed", "rejected", false)]
    public async Task AcNgr001FreshCurrentJobPolicyRequiresExactIdentityAndAllowedState(
        string corruption, string expectedOutput, bool accepted)
    {
        var root = IsolatedAggregateNodeProcess.RepositoryRoot();
        var module = IsolatedAggregateNodeProcess.Module("isolated-current-job.mjs");
        var fixture = Path.Combine(root, "tests", "KeyLoad.ComparisonTests", "Features", "BenchmarkComparisons", "UnitContracts",
            "IsolatedCurrentJobOriginal.json");
        var result = await IsolatedAggregateNodeProcess.RunAsync(
            ["--input-type=module", "-e", IsolatedCurrentJobNodeProgram.Policy, module, fixture, corruption],
            TestContext.Current!.Execution.CancellationToken);

        await Assert.That(result.ExitCode).IsEqualTo(accepted ? 0 : 1);
        await Assert.That(result.Output.Trim()).IsEqualTo(expectedOutput);
        await Assert.That(result.Error).IsEmpty();
    }
}
