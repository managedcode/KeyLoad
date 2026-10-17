namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class NativeBoundLeaseDisposalTests
{
    [Test]
    public Task BoundAsyncDisposalJoinsRealChildAndAllowsNextQuery()
        => NativeBoundLeaseDisposalFlow.ExecuteAsync(false, false, TestContext.Current!.Execution.CancellationToken);

    [Test]
    public Task BoundAsyncDisposalPreservesFirstOriginalAndCleanupThroughRealChild()
        => NativeBoundLeaseDisposalFlow.ExecuteAsync(true, false, TestContext.Current!.Execution.CancellationToken);

    [Test]
    public Task BoundAsyncDisposalJoinsCompletionThatAlreadyChoseNullContext()
        => NativeBoundLeaseDisposalFlow.ExecuteAsync(false, true, TestContext.Current!.Execution.CancellationToken);

    [Test]
    public Task BoundAsyncDisposalJoinsCompletionThatAlreadyChoseFailures()
        => NativeBoundLeaseDisposalFlow.ExecuteAsync(true, true, TestContext.Current!.Execution.CancellationToken);
}
