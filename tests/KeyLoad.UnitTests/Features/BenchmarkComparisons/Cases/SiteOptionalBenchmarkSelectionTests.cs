namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>AC-BC-WEB-006: the production Website context admits only its current push/manual executors.</summary>
internal sealed class SiteOptionalBenchmarkSelectionTests
{
    [Test]
    [Arguments("website-executor-push", true)]
    [Arguments("website-executor-manual", true)]
    [Arguments("website-executor-retired-event", false)]
    [Arguments("website-executor-ci", false)]
    [Arguments("website-executor-foreign-path", false)]
    [Arguments("website-executor-unsupported-job", false)]
    [Arguments("optional-arguments", true)]
    public async Task AcBcWeb006WebsiteAdmissionAndOptionalArgumentsUseCurrentContext(string scenario, bool accepted)
    {
        var result = await SiteOptionalBenchmarkSelectionNodeProcess.RunAsync(scenario,
            TestContext.Current!.Execution.CancellationToken);
        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.Output.Trim()).IsEqualTo(accepted ? "accepted" : "rejected");
        await Assert.That(result.Error).IsEmpty();
    }
}
