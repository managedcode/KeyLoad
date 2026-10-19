using KeyLoad.Comparisons;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>AC-METH-004: failed execution admission joins owned cleanup and retains its original failure.</summary>
internal sealed class DocumentAcquisitionTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AcMeth004AdmissionRetainsPrimaryAndJoinedCleanup(bool cleanupFails)
    {
        var primary = RuntimeFailureFixture.RuntimeOversizeFailure();
        var cleanup = cleanupFails ? new InvalidOperationException() : null;
        await using var client = new AdmissionClient(cleanup);
        Exception? observed = null;
        try
        {
            _ = await DocumentSessionAcquisition<AdmissionClient>.OpenAsync(
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
        internal int Disposals
        {
            get; private set;
        }
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
