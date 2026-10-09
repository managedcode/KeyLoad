using KeyLoad.Comparisons;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>AC-METH-004: failed native admission joins owned cleanup and retains its original failure.</summary>
internal sealed class DocumentNativeAcquisitionTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AcMeth004NativeAdmissionRetainsPrimaryAndJoinedCleanup(bool cleanupFails)
    {
        var primary = OpenLoopOriginalFailureReadFixture.RuntimeOversizeFailure();
        var cleanup = cleanupFails ? new InvalidOperationException() : null;
        await using var client = new AdmissionClient(cleanup);
        Exception? observed = null;
        try
        {
            _ = await DocumentNativeSessionAcquisition<AdmissionClient>.OpenAsync(
                _ => Task.FromResult(client), (_, _) => Task.FromException(primary),
                _ => throw new InvalidOperationException(), TestContext.Current!.Execution.CancellationToken);
        }
        catch (Exception failure) when (failure is OutOfMemoryException or AggregateException)
        {
            observed = failure;
        }
        await Assert.That(client.Disposals).IsEqualTo(1);
        if (cleanupFails)
        {
            var failures = (observed as AggregateException)?.InnerExceptions;
            await Assert.That(failures is not null && failures.Any(item => ReferenceEquals(item, primary))
                && failures.Any(item => ReferenceEquals(item, cleanup))).IsTrue();
        }
        else
        {
            await Assert.That(ReferenceEquals(observed, primary)).IsTrue();
        }
    }
    private sealed class AdmissionClient(Exception? cleanup) : IAsyncDisposable
    {
        internal int Disposals { get; private set; }
        public ValueTask DisposeAsync()
        {
            if (Disposals > 0)
            {
                return ValueTask.CompletedTask;
            }
            Disposals++;
            return cleanup is null ? ValueTask.CompletedTask : ValueTask.FromException(cleanup);
        }
    }
}
