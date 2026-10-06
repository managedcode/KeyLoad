namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>AC-BC-WEB-002: optional selection consumes authentic retained GitHub metadata and remains strict for ready evidence.</summary>
internal sealed class SiteOptionalBenchmarkSelectionTests
{
    [Test]
    [Arguments("retained-success", true)]
    [Arguments("retained-unready", true)]
    [Arguments("failed-aggregate", true)]
    [Arguments("no-aggregate", true)]
    [Arguments("current-success", true)]
    [Arguments("invalid-latest-ready", true)]
    [Arguments("ordered-producers", true)]
    [Arguments("empty-workflow-identity", true)]
    [Arguments("source-contract-routes", true)]
    [Arguments("optional-arguments", true)]
    [Arguments("freshness-null-null", true)]
    [Arguments("freshness-source-change", true)]
    [Arguments("freshness-null-to-ready", true)]
    [Arguments("freshness-ready-to-null", true)]
    [Arguments("freshness-tamper", true)]
    [Arguments("legacy-plan", true)]
    [Arguments("legacy-plan-reject-changed", true)]
    [Arguments("legacy-plan-reject-source", true)]
    [Arguments("website-executor-accepted", true)]
    [Arguments("website-executor-legacy-ci", true)]
    [Arguments("website-executor-wrong-path", true)]
    public async Task AcBcWeb002OptionalSelectionKeepsAuthenticAggregateAndCurrentStepContracts(string scenario, bool accepted)
    {
        var result = await SiteOptionalBenchmarkSelectionNodeProcess.RunAsync(scenario,
            TestContext.Current!.Execution.CancellationToken);
        await Assert.That(result.ExitCode).IsEqualTo(accepted ? 0 : 1);
        await Assert.That(result.Output.Trim()).IsEqualTo(accepted ? "accepted" : "rejected");
        await Assert.That(result.Error).IsEmpty();
    }
}
