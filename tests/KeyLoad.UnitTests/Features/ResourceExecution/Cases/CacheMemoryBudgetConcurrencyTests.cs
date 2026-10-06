using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class CacheMemoryBudgetConcurrencyTests
{
    private static readonly TimeSpan CoordinationTimeout = TimeSpan.FromSeconds(10);

    [Test]
    public async Task SimultaneousConsumersCannotExceedByteOrEntryCeilingsAndReleaseConcurrently()
    {
        const int ConsumerCount = 8;
        using var budget = new CacheMemoryBudget(UnitAdmissionOptions.Cache(new()
        {
            MaxRetainedBytes = ConsumerCount,
            MaxRetainedEntries = ConsumerCount
        }));
        using var ready = new CountdownEvent(ConsumerCount);
        var allReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var consumers = Enumerable.Range(0, ConsumerCount).Select(_ => Task.Run(async () =>
        {
            using var reservation = Reserve(budget, 1, 1);
            if (ready.Signal())
            {
                allReady.TrySetResult();
            }
            await release.Task.ConfigureAwait(false);
            await Task.Yield();
            reservation.Dispose();
            reservation.Dispose();
        })).ToArray();

        try
        {
            await Assert.That(await WaitForReadyAsync(allReady.Task, CoordinationTimeout,
                TestContext.Current!.Execution.CancellationToken)).IsTrue();
            var full = budget.GetSnapshot();
            await Assert.That(full).IsEqualTo(new CacheMemorySnapshot(ConsumerCount, ConsumerCount,
                ConsumerCount, false, ConsumerCount, ConsumerCount));
            var extraAdmitted = budget.TryReserve(1, 0, out var rejected);
            using var rejectedLifetime = rejected;
            await Assert.That(extraAdmitted).IsFalse();
            await Assert.That(rejectedLifetime).IsNull();
            await AssertSnapshotUnchanged(budget, full);
        }
        finally
        {
            release.TrySetResult();
            await Task.WhenAll(consumers).WaitAsync(CoordinationTimeout, TimeProvider.System);
        }

        await Assert.That(budget.GetSnapshot()).IsEqualTo(new CacheMemorySnapshot(0, 0, 0,
            false, ConsumerCount, ConsumerCount));
    }

    [Test]
    public async Task ConcurrentRepeatedDisposalReleasesOneReservationExactlyOnce()
    {
        const int Disposers = 32;
        using var budget = new CacheMemoryBudget(UnitAdmissionOptions.Cache(new() { MaxRetainedBytes = 64, MaxRetainedEntries = 64 }));
        using var reservation = Reserve(budget, 17, 3);
        using var ready = new CountdownEvent(Disposers);
        var allReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposals = Enumerable.Range(0, Disposers).Select(_ => Task.Run(async () =>
        {
            if (ready.Signal())
            {
                allReady.TrySetResult();
            }
            await release.Task.ConfigureAwait(false);
            reservation.Dispose();
        })).ToArray();

        try
        {
            await Assert.That(await WaitForReadyAsync(allReady.Task, CoordinationTimeout,
                TestContext.Current!.Execution.CancellationToken)).IsTrue();
        }
        finally
        {
            release.TrySetResult();
            await Task.WhenAll(disposals).WaitAsync(CoordinationTimeout, TimeProvider.System);
        }

        reservation.Dispose();
        await Assert.That(budget.GetSnapshot()).IsEqualTo(new CacheMemorySnapshot(0, 0, 0,
            false, 64, 64));
    }

    [Test]
    public async Task ClosingPoolRejectsAdmissionAndKeepsOutstandingChargeVisibleUntilRelease()
    {
        using var budget = new CacheMemoryBudget(UnitAdmissionOptions.Cache(new() { MaxRetainedBytes = 8, MaxRetainedEntries = 2 }));
        using var reservation = Reserve(budget, 4, 1);
        budget.Dispose();
        budget.Dispose();

        var closed = new CacheMemorySnapshot(4, 1, 1, true, 8, 2);
        await Assert.That(budget.GetSnapshot()).IsEqualTo(closed);
        var admitted = budget.TryReserve(1, 0, out var rejected);
        using var rejectedLifetime = rejected;
        await Assert.That(admitted).IsFalse();
        await Assert.That(rejectedLifetime).IsNull();
        await AssertSnapshotUnchanged(budget, closed);

        reservation.Dispose();
        reservation.Dispose();
        await Assert.That(budget.GetSnapshot()).IsEqualTo(new CacheMemorySnapshot(0, 0, 0, true, 8, 2));
        budget.Dispose();
        await Assert.That(budget.GetSnapshot()).IsEqualTo(new CacheMemorySnapshot(0, 0, 0, true, 8, 2));
    }

    private static ICacheMemoryReservation Reserve(CacheMemoryBudget budget, long bytes, int entries)
    {
        if (!budget.TryReserve(bytes, entries, out var reservation) || reservation is null)
        {
            throw new InvalidOperationException("The test reservation was unexpectedly rejected.");
        }

        return reservation;
    }

    private static async Task AssertSnapshotUnchanged(CacheMemoryBudget budget, CacheMemorySnapshot expected)
        => await Assert.That(budget.GetSnapshot()).IsEqualTo(expected);

    private static async Task<bool> WaitForReadyAsync(Task ready, TimeSpan timeout, CancellationToken cancellationToken)
    {
        try
        {
            await ready.WaitAsync(timeout, TimeProvider.System, cancellationToken);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }
}
