using KeyLoad.UnitTests.Features.CodeQuality.Helpers;

namespace KeyLoad.UnitTests.Features.CodeQuality.Cases;

internal sealed class ProductionSourceManifestOperationTests
{
    [Test]
    public async Task NativeImagesProduceClosedManifestAndRejectTamperingWithoutReplacingEvidence()
    {
        await ProductionSourceManifestScenario.RunOwnedAsync(TestContext.Current!.Execution.CancellationToken);
    }
}
