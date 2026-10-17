namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal sealed class NativeSubmissionFaultRf3Tests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task GenuinePostCommitLogFaultReturnsUnknownAndOriginalCommandReconciles(bool officialMcp)
        => NativeSubmissionFaultRf3Owner.RunAsync(officialMcp, TestContext.Current!.Execution.CancellationToken);
}
