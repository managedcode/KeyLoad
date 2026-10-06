using KeyLoad.Comparisons;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class OpenLoopOriginalFailureWorkerTests
{
    private const string NativeFatalWrapper = "Native operation retained its fatal failure.";
    private const string NativeFatalSibling = "Native operation retained its sibling failure.";
    private const string ProgressFailure = "Native progress callback failed.";
    private const string CancellationFailure = "Native cancellation callback failed.";

    [Test]
    public async Task FatalOperationAndProgressFailureRetainOriginalsAndJoinAllCancellationCallbacks()
    {
        var fatal = OpenLoopOriginalFailureReadFixture.RuntimeOversizeFailure();
        var sibling = new IOException(NativeFatalSibling);
        var operation = new AggregateException(new InvalidOperationException(NativeFatalWrapper, fatal), sibling);
        var observer = new IOException(ProgressFailure);
        var cancellationFailure = new InvalidOperationException(CancellationFailure);
        using var lifetime = new CancellationTokenSource();
        var callbackCount = 0;
        var progressCount = 0;
        OpenLoopProgressV1? observedProgress = null;
        using var first = lifetime.Token.Register(() => Interlocked.Increment(ref callbackCount));
        using var second = lifetime.Token.Register(() =>
        {
            Interlocked.Increment(ref callbackCount);
            throw cancellationFailure;
        });
        var progress = Progress();

        var failure = (await Assert.ThrowsExactlyAsync<AggregateException>(() =>
            OpenLoopWorker.PublishProgressAsync(progress, value =>
            {
                observedProgress = value;
                Interlocked.Increment(ref progressCount);
                throw observer;
            }, operation, lifetime)))!;

        await Assert.That(failure.InnerExceptions.Count).IsEqualTo(4);
        await Assert.That(failure.InnerExceptions[0]).IsSameReferenceAs(fatal);
        await Assert.That(failure.InnerExceptions[1]).IsSameReferenceAs(operation);
        await Assert.That(failure.InnerExceptions[2]).IsSameReferenceAs(observer);
        var cancellation = failure.InnerExceptions[3] as AggregateException;
        await Assert.That(cancellation).IsNotNull();
        var callbackFailure = await Assert.That(cancellation!.InnerExceptions).HasSingleItem();
        await Assert.That(callbackFailure).IsSameReferenceAs(cancellationFailure);
        await Assert.That(operation.InnerExceptions[1]).IsSameReferenceAs(sibling);
        await Assert.That(observedProgress).IsSameReferenceAs(progress);
        await Assert.That(progressCount).IsEqualTo(1);
        await Assert.That(callbackCount).IsEqualTo(2);
        await Assert.That(lifetime.IsCancellationRequested).IsTrue();
    }

    [Test]
    public async Task FatalWithoutProgressStillCancelsOwnerAndRetainsWholeOriginalAggregate()
    {
        var fatal = OpenLoopOriginalFailureReadFixture.RuntimeOversizeFailure();
        var sibling = new IOException(NativeFatalSibling);
        var operation = new AggregateException(new InvalidOperationException(NativeFatalWrapper, fatal), sibling);
        using var lifetime = new CancellationTokenSource();
        var callbackCount = 0;
        using var registration = lifetime.Token.Register(() => Interlocked.Increment(ref callbackCount));

        var failure = (await Assert.ThrowsExactlyAsync<AggregateException>(() =>
            OpenLoopWorker.PublishProgressAsync(null, null, operation, lifetime)))!;

        await Assert.That(failure.InnerExceptions.Count).IsEqualTo(2);
        await Assert.That(failure.InnerExceptions[0]).IsSameReferenceAs(fatal);
        await Assert.That(failure.InnerExceptions[1]).IsSameReferenceAs(operation);
        await Assert.That(operation.InnerExceptions[1]).IsSameReferenceAs(sibling);
        await Assert.That(callbackCount).IsEqualTo(1);
        await Assert.That(lifetime.IsCancellationRequested).IsTrue();
    }

    [Test]
    public async Task SoleProgressFailureRethrowsSameObjectAfterOwnerCancellationSettles()
    {
        var observer = new IOException(ProgressFailure);
        using var lifetime = new CancellationTokenSource();
        var callbackCount = 0;
        using var registration = lifetime.Token.Register(() => Interlocked.Increment(ref callbackCount));

        var failure = await Assert.ThrowsExactlyAsync<IOException>(() =>
            OpenLoopWorker.PublishProgressAsync(Progress(), _ => throw observer, null, lifetime));

        await Assert.That(failure).IsSameReferenceAs(observer);
        await Assert.That(callbackCount).IsEqualTo(1);
        await Assert.That(lifetime.IsCancellationRequested).IsTrue();
    }

    [Test]
    public async Task SoleFatalRethrowsOriginalObjectAfterOwnerCancellationSettles()
    {
        var fatal = OpenLoopOriginalFailureReadFixture.RuntimeOversizeFailure();
        using var lifetime = new CancellationTokenSource();
        var callbackCount = 0;
        using var registration = lifetime.Token.Register(() => Interlocked.Increment(ref callbackCount));

        var failure = await Assert.ThrowsExactlyAsync<OutOfMemoryException>(() =>
            OpenLoopWorker.PublishProgressAsync(null, null, fatal, lifetime));

        await Assert.That(failure).IsSameReferenceAs(fatal);
        await Assert.That(callbackCount).IsEqualTo(1);
        await Assert.That(lifetime.IsCancellationRequested).IsTrue();
    }

    private static OpenLoopProgressV1 Progress()
        => new(OpenLoopRateContract.ProgressInterval, OpenLoopRateContract.PlannedOperations,
            OpenLoopRateContract.ProgressInterval, OpenLoopRateContract.LowLoadRatePerSecond, Scenario.PointRead);
}
