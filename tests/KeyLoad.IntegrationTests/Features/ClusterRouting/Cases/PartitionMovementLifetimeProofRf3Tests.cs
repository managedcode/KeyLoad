namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

[NotInParallel]
internal sealed class PartitionMovementLifetimeProofRf3Tests
{
    [Test]
    public Task PublicSdkAndOfficialMcpRefuseInvalidLifetimeCarriersThenReplayExactEffectsAcrossSameRootColdContinuation()
        => PartitionMovementLifetimeProofRf3Trial.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
