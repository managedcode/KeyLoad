namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Preserves the original BlobWire case through its genuine six-silo Aspire native successor.</summary>
[NotInParallel]
internal sealed class PartitionMovementBlobWireNativeTests
{
    [Test]
    public Task ActualSignedSixKindBlobWireFaultsDenyWithoutEffectsThenOriginalSendAndColdSdkMcpQ1AreHealthy()
        => NativeBlobWholeParentTrial.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
