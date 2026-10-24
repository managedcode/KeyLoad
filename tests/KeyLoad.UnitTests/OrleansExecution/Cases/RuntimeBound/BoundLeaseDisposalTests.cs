namespace KeyLoad.UnitTests.OrleansExecution;

internal sealed class BoundLeaseDisposalTests
{
    [Test]
    public Task BoundAsyncDisposalPreservesQueryAndAllowsNextQuery()
        => BoundLeaseDisposalFlow.ExecuteAsync(false, false, TestContext.Current!.Execution.CancellationToken);

    [Test]
    public Task BoundAsyncDisposalPreservesFirstOriginalAndCleanup()
        => BoundLeaseDisposalFlow.ExecuteAsync(true, false, TestContext.Current!.Execution.CancellationToken);

    [Test]
    public Task BoundAsyncDisposalJoinsCompletionThatAlreadyChoseNullContext()
        => BoundLeaseDisposalFlow.ExecuteAsync(false, true, TestContext.Current!.Execution.CancellationToken);

    [Test]
    public Task BoundAsyncDisposalJoinsCompletionThatAlreadyChoseFailures()
        => BoundLeaseDisposalFlow.ExecuteAsync(true, true, TestContext.Current!.Execution.CancellationToken);
}
