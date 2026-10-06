using KeyLoad.Comparisons;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class OpenLoopOriginalFailureTests
{
    private const string ReadFailure = "Original health read failed.";
    private const string DisposalFailure = "Original health session disposal failed.";
    private const string FatalReadSibling = "Original sibling of fatal health read failed.";

    [Test]
    public async Task SuccessfulReadReturnsOriginalResultOnlyAfterDisposalCompletes()
    {
        var reference = new EntityRef(new("tenant", "database", "domain", "partition"), "documents", "document");
        var result = new OpenLoopCancellationHealthRead(reference, new(reference, 1, "{}", false, []));
        await using var owner = new OpenLoopOriginalFailureReadFixture();
        owner.CompleteRead(result);
        await owner.DisposalStarted.WaitAsync(TestContext.Current!.Execution.CancellationToken);
        await Assert.That(owner.Completion.IsCompleted).IsFalse();
        owner.CompleteDisposal();

        await Assert.That(await owner.Completion).IsSameReferenceAs(result);
        await Assert.That(owner.DisposeCalls).IsEqualTo(1);
        await Assert.That(owner.ReadCompletedBeforeDispose).IsTrue();
        await Assert.That(owner.DisposalCompleted).IsTrue();
    }

    [Test]
    public async Task HealthReadAndDisposalFailuresRetainBothOriginalObjectsAfterSettlement()
    {
        var primary = new IOException(ReadFailure);
        var cleanup = new InvalidOperationException(DisposalFailure);
        await using var owner = new OpenLoopOriginalFailureReadFixture();
        await Assert.That(owner.DisposeCalls).IsEqualTo(0);
        owner.FailRead(primary);
        await owner.DisposalStarted.WaitAsync(TestContext.Current!.Execution.CancellationToken);
        await Assert.That(owner.Completion.IsCompleted).IsFalse();
        owner.FailDisposal(cleanup);

        var failure = (await Assert.ThrowsExactlyAsync<AggregateException>(() => owner.Completion))!;
        await Assert.That(failure.InnerExceptions.Count).IsEqualTo(2);
        await Assert.That(failure.InnerExceptions[0]).IsSameReferenceAs(primary);
        await Assert.That(failure.InnerExceptions[1]).IsSameReferenceAs(cleanup);
        await AssertSettledAsync(owner);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task WrappedFatalInEitherStageHasPriorityWhileOriginalFailuresRemain(bool fatalInDisposal)
    {
        var fatal = new OutOfMemoryException();
        var sibling = new IOException(FatalReadSibling);
        var wrapped = new AggregateException(new InvalidOperationException(ReadFailure, fatal), sibling);
        var ordinary = new IOException(DisposalFailure);
        Exception primary = fatalInDisposal ? ordinary : wrapped;
        Exception cleanup = fatalInDisposal ? wrapped : ordinary;
        await using var owner = new OpenLoopOriginalFailureReadFixture();
        owner.FailRead(primary);
        await owner.DisposalStarted.WaitAsync(TestContext.Current!.Execution.CancellationToken);
        await Assert.That(owner.Completion.IsCompleted).IsFalse();
        owner.FailDisposal(cleanup);

        var failure = (await Assert.ThrowsExactlyAsync<AggregateException>(() => owner.Completion))!;
        await Assert.That(failure.InnerExceptions.Count).IsEqualTo(3);
        await Assert.That(failure.InnerExceptions[0]).IsSameReferenceAs(fatal);
        await Assert.That(failure.InnerExceptions[1]).IsSameReferenceAs(primary);
        await Assert.That(failure.InnerExceptions[2]).IsSameReferenceAs(cleanup);
        await Assert.That(wrapped.InnerExceptions[1]).IsSameReferenceAs(sibling);
        await AssertSettledAsync(owner);
    }

    [Test]
    public async Task CanceledOriginalReadStillJoinsDisposalAndPreservesItsTokenAndCleanupObject()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var cleanup = new IOException(DisposalFailure);
        await using var owner = new OpenLoopOriginalFailureReadFixture();
        owner.CancelRead(cancellation.Token);
        await owner.DisposalStarted.WaitAsync(TestContext.Current!.Execution.CancellationToken);
        await Assert.That(owner.Completion.IsCompleted).IsFalse();
        owner.FailDisposal(cleanup);

        var failure = (await Assert.ThrowsExactlyAsync<AggregateException>(() => owner.Completion))!;
        await Assert.That(owner.ReadWasCanceled).IsTrue();
        await Assert.That(failure.InnerExceptions.Count).IsEqualTo(2);
        var canceled = failure.InnerExceptions[0] as TaskCanceledException;
        await Assert.That(canceled).IsNotNull();
        await Assert.That(canceled!.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(failure.InnerExceptions[1]).IsSameReferenceAs(cleanup);
        await AssertSettledAsync(owner);
    }

    [Test]
    public async Task SynchronousDisposalThrowRetainsBothFailuresThroughNativeAsyncBoundary()
    {
        var primary = new IOException(ReadFailure);
        var cleanup = new InvalidOperationException(DisposalFailure);
        var disposeCalls = 0;
        var original = Task.FromException<OpenLoopCancellationHealthRead>(primary);
        var completion = OpenLoopHealthyReadVerifier.SettleReadAsync(original, () =>
        {
            Interlocked.Increment(ref disposeCalls);
            throw cleanup;
        });

        var failure = (await Assert.ThrowsExactlyAsync<AggregateException>(() => completion))!;
        await Assert.That(failure.InnerExceptions.Count).IsEqualTo(2);
        await Assert.That(failure.InnerExceptions[0]).IsSameReferenceAs(primary);
        await Assert.That(failure.InnerExceptions[1]).IsSameReferenceAs(cleanup);
        await Assert.That(disposeCalls).IsEqualTo(1);
        await Assert.That(original.IsFaulted).IsTrue();
        await Assert.That(completion.IsFaulted).IsTrue();
    }

    private static async Task AssertSettledAsync(OpenLoopOriginalFailureReadFixture owner)
    {
        await Assert.That(owner.DisposeCalls).IsEqualTo(1);
        await Assert.That(owner.ReadCompletedBeforeDispose).IsTrue();
        await Assert.That(owner.DisposalCompleted).IsTrue();
        await Assert.That(owner.Completion.IsFaulted).IsTrue();
    }
}
