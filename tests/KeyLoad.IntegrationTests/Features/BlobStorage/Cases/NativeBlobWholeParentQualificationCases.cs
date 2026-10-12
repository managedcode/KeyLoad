using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal sealed class NativeBlobWholeParentQualificationCases
{
    [Test]
    public Task ActualSdkMcpAndQ1OriginalBlobFaultFlowPreservesAllSixStoresAcrossTwoColdReopens()
        => NativeBlobWholeParentTrial.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
