namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Actual CLR task outcome inputs; these tests do not qualify Docker or native cancellation.</summary>
internal sealed class MongoReadinessTaskFailureTests
{
    private const string Failure = "ActualTaskFailure";
    private const int IndependentFailures = 2;

    /// <summary>AC-MR-031-004/AC-ISO-006/007: cancellation is retained even when another original faults.</summary>
    [Test]
    public async Task ActualCancellationAndIndependentIoFaultKeepBothExceptionIdentities()
    {
        using var source = new CancellationTokenSource();
        await source.CancelAsync();
        var cancelled = CancelledAsync(source.Token);
        var input = new IOException(Failure);
        var faulted = FaultedAsync(input);
        var cancellation = await Assert.ThrowsAsync<OperationCanceledException>(() => cancelled);
        var failure = await Assert.ThrowsAsync<AggregateException>(() => MongoNativeReadinessRegressionFailures.JoinAsync(cancelled, faulted))
            ?? throw new InvalidOperationException(Failure);
        await Assert.That(failure.InnerExceptions.Count).IsEqualTo(IndependentFailures);
        await Assert.That(failure.InnerExceptions.OfType<OperationCanceledException>().Single()).IsSameReferenceAs(cancellation);
        await Assert.That(failure.InnerExceptions.OfType<OperationCanceledException>().Single().CancellationToken).IsEqualTo(source.Token);
        await Assert.That(failure.InnerExceptions.OfType<IOException>().Single()).IsSameReferenceAs(input);
    }

    /// <summary>AC-MR-031-004/AC-ISO-006/007: sole actual failure retains its original exception identity.</summary>
    [Test]
    public async Task SoleIoFailureRetainsOriginalIdentity()
    {
        var input = new IOException(Failure);
        var failure = await Assert.ThrowsAsync<IOException>(() => MongoNativeReadinessRegressionFailures.JoinAsync(FaultedAsync(input)));
        await Assert.That(failure).IsSameReferenceAs(input);
    }

    /// <summary>AC-MR-031-004/AC-ISO-006/007: actual successful work completes the same join.</summary>
    [Test]
    public async Task RealCompletedTasksJoinSuccessfully()
    {
        var first = CompletedAsync();
        var second = CompletedAsync();
        await MongoNativeReadinessRegressionFailures.JoinAsync(first, second);
        await Assert.That(first.IsCompletedSuccessfully && second.IsCompletedSuccessfully).IsTrue();
    }

    private static async Task CancelledAsync(CancellationToken token) => await Task.Delay(Timeout.InfiniteTimeSpan, TimeProvider.System, token);

    private static async Task FaultedAsync(IOException failure)
    {
        await Task.Yield();
        throw failure;
    }

    private static async Task CompletedAsync() => await Task.Yield();
}
