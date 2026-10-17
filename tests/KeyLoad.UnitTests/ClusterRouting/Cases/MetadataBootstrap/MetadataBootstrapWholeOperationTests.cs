namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class MetadataBootstrapWholeOperationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task ConcurrentNativeBootstrapHasOneIdentityThenPersistedAdminFencesRepairTriadReplayAndTwoCold(bool conflicting)
        => MetadataBootstrapFlow.RunAsync(conflicting, TestContext.Current!.Execution.CancellationToken);
}
