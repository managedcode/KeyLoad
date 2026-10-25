using KeyLoad.FunctionalTests.CodeQuality.Helpers;

namespace KeyLoad.FunctionalTests.CodeQuality.Cases;

[NotInParallel]
internal sealed class ProductionSourceManifestOperationTests
{
    [Test]
    public async Task ImagesProduceClosedManifestAndRejectTamperingWithoutReplacingEvidence()
    {
        await ProductionSourceManifestScenario.RunOwnedAsync(TestContext.Current!.Execution.CancellationToken);
    }
}
