namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Supporting six native production owners; mandatory Docker RF3 evidence remains separate.</summary>
[NotInParallel]
internal sealed class PartitionMovementBlobWireNativeTests
{
    [Test]
    public Task ActualSignedSixKindBlobWireFaultsDenyWithoutEffectsThenOriginalSendAndColdSdkMcpQ1AreHealthy()
        => PartitionMovementBlobWireTrial.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
