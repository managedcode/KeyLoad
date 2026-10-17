namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal sealed class ReplicaForwardMetadataNativeTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    public Task GenuineNonleaderForwardImportsOriginalMetadataOnceAndKnownCanonicalRefusalKeepsBudgetUsable(bool removeGrant, bool exhaustReceiver)
        => ReplicaForwardMetadataFlow.RunAsync(removeGrant, exhaustReceiver, TestContext.Current!.Execution.CancellationToken);
}
