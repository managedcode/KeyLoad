namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

[NotInParallel]
internal sealed class ImageRegistryReadinessTests
{
    private const string FixtureName = "ImageRegistryReadinessFixture.mjs";
    private const string ModuleName = "image-manifest.mjs";
    private const string TestsDirectory = "tests";
    private const string ProjectDirectory = "KeyLoad.ComparisonTests";
    private const string FeaturesDirectory = "Features";
    private const string SliceDirectory = "BenchmarkComparisons";
    private const string AcceptedPrefix = "accepted:";

    [Test]
    [Arguments("healthy")]
    [Arguments("hung-first")]
    [Arguments("slow-first")]
    [Arguments("status-first")]
    [Arguments("reset-first")]
    [Arguments("redirect-first")]
    [Arguments("evidence-unsafe")]
    [Arguments("evidence-bounds")]
    [Arguments("status-deadline")]
    [Arguments("hung-deadline")]
    public async Task AcBcFail006ReadinessPreservesActualHttpBudgetsAndPrivateEvidence(string scenario)
    {
        using var directory = new ImageBundleTestDirectory();
        var fixture = Path.Combine(IsolatedAggregateNodeProcess.RepositoryRoot(), TestsDirectory, ProjectDirectory,
            FeaturesDirectory, SliceDirectory, "UnitContracts", "Processes", FixtureName);
        var result = await IsolatedAggregateNodeProcess.RunAsync(
            [fixture, IsolatedAggregateNodeProcess.Module(ModuleName), scenario, directory.Root],
            TestContext.Current!.Execution.CancellationToken);
        await Assert.That(result.Error).IsEmpty();
        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.Output.Trim()).IsEqualTo(AcceptedPrefix + scenario);
    }
}
