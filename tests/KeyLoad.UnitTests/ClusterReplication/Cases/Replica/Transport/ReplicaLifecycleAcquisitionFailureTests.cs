namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal sealed class ReplicaLifecycleAcquisitionFailureTests
{
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public Task OriginalNativeAcquisitionRefusalReleasesEarlierOwnersThenSameRootApplies(bool replicaLocked)
        => ReplicaLifecycleAcquisitionFailureFlow.RunAsync(replicaLocked, TestContext.Current!.Execution.CancellationToken);
}
