namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class NativeBoundLeaseDisposalTests
{
    [Test]
    public Task BoundAsyncDisposalPreservesQueryAndAllowsNextQuery()
        => NativeBoundLeaseDisposalFlow.ExecuteAsync(false, false, TestContext.Current!.Execution.CancellationToken);

    [Test]
    public Task BoundAsyncDisposalPreservesFirstOriginalAndCleanup()
        => NativeBoundLeaseDisposalFlow.ExecuteAsync(true, false, TestContext.Current!.Execution.CancellationToken);

    [Test]
    public Task BoundAsyncDisposalJoinsCompletionThatAlreadyChoseNullContext()
        => NativeBoundLeaseDisposalFlow.ExecuteAsync(false, true, TestContext.Current!.Execution.CancellationToken);

    [Test]
    public Task BoundAsyncDisposalJoinsCompletionThatAlreadyChoseFailures()
        => NativeBoundLeaseDisposalFlow.ExecuteAsync(true, true, TestContext.Current!.Execution.CancellationToken);
}
