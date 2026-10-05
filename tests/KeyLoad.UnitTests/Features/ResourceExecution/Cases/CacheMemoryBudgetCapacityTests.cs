using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class CacheMemoryBudgetCapacityTests
{
    private const long BytesAboveMaximum = 1_073_741_825;
    private const int EntriesAboveMaximum = 65_537;

    [Test]
    public async Task ExactByteAndEntryCeilingsAdmitWhileOneOverLeavesAccountingUnchanged()
    {
        using var budget = new CacheMemoryBudget(UnitAdmissionOptions.Cache(new() { MaxRetainedBytes = 10, MaxRetainedEntries = 2 }));
        using var bytes = Reserve(budget, 10, 1);
        await Assert.That(budget.GetSnapshot()).IsEqualTo(new CacheMemorySnapshot(10, 1, 1, false, 10, 2));

        var full = budget.GetSnapshot();
        var extraBytesAdmitted = budget.TryReserve(1, 0, out var rejectedBytes);
        using var rejectedBytesLifetime = rejectedBytes;
        await Assert.That(extraBytesAdmitted).IsFalse();
        await Assert.That(rejectedBytesLifetime).IsNull();
        await Assert.That(budget.GetSnapshot()).IsEqualTo(full);

        bytes.Dispose();
        using var entries = Reserve(budget, 1, 2);
        await Assert.That(budget.GetSnapshot()).IsEqualTo(new CacheMemorySnapshot(1, 2, 1, false, 10, 2));
        full = budget.GetSnapshot();
        var extraEntryAdmitted = budget.TryReserve(1, 1, out var rejectedEntries);
        using var rejectedEntriesLifetime = rejectedEntries;
        await Assert.That(extraEntryAdmitted).IsFalse();
        await Assert.That(rejectedEntriesLifetime).IsNull();
        await Assert.That(budget.GetSnapshot()).IsEqualTo(full);
    }

    [Test]
    public async Task PositiveByteIndexBaselineCanReserveZeroEntries()
    {
        using var budget = new CacheMemoryBudget(UnitAdmissionOptions.Cache(new() { MaxRetainedBytes = 4, MaxRetainedEntries = 1 }));
        using var index = Reserve(budget, 4, 0);

        await Assert.That(index.Bytes).IsEqualTo(4);
        await Assert.That(index.Entries).IsEqualTo(0);
        await Assert.That(budget.GetSnapshot()).IsEqualTo(new CacheMemorySnapshot(4, 0, 1, false, 4, 1));
    }

    [Test]
    public async Task InvalidLimitsAndRequestsRejectBeforeChangingAccounting()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => CreateAndUseBudget(null!));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            CreateAndUseBudget(new() { MaxRetainedBytes = 0 }));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            CreateAndUseBudget(new() { MaxRetainedBytes = -1 }));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            CreateAndUseBudget(new() { MaxRetainedEntries = 0 }));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            CreateAndUseBudget(new() { MaxRetainedEntries = -1 }));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            CreateAndUseBudget(new() { MaxRetainedBytes = BytesAboveMaximum }));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            CreateAndUseBudget(new() { MaxRetainedEntries = EntriesAboveMaximum }));

        using var budget = new CacheMemoryBudget(UnitAdmissionOptions.Cache(new() { MaxRetainedBytes = 8, MaxRetainedEntries = 2 }));
        var before = budget.GetSnapshot();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ReserveInvalidRequest(budget, 0, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ReserveInvalidRequest(budget, -1, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ReserveInvalidRequest(budget, 1, -1));
        await Assert.That(budget.GetSnapshot()).IsEqualTo(before);
    }

    [Test]
    public async Task OverflowSizedPositiveRequestsFailWithoutWrappingAccounting()
    {
        using var budget = new CacheMemoryBudget(UnitAdmissionOptions.Cache(new() { MaxRetainedBytes = 8, MaxRetainedEntries = 4 }));
        using var first = Reserve(budget, 7, 1);
        var before = budget.GetSnapshot();

        var oversizedAdmitted = budget.TryReserve(long.MaxValue, 0, out var oversized);
        using var oversizedLifetime = oversized;
        await Assert.That(oversizedAdmitted).IsFalse();
        await Assert.That(oversizedLifetime).IsNull();
        await Assert.That(budget.GetSnapshot()).IsEqualTo(before);
        var tooManyEntriesAdmitted = budget.TryReserve(2, int.MaxValue, out var tooManyEntries);
        using var tooManyEntriesLifetime = tooManyEntries;
        await Assert.That(tooManyEntriesAdmitted).IsFalse();
        await Assert.That(tooManyEntriesLifetime).IsNull();
        await Assert.That(budget.GetSnapshot()).IsEqualTo(before);
    }

    private static ICacheMemoryReservation Reserve(CacheMemoryBudget budget, long bytes, int entries)
    {
        if (!budget.TryReserve(bytes, entries, out var reservation) || reservation is null)
        {
            throw new InvalidOperationException("The test reservation was unexpectedly rejected.");
        }

        return reservation;
    }

    private static void CreateAndUseBudget(CacheMemoryLimits limits)
    {
        using var budget = new CacheMemoryBudget(UnitAdmissionOptions.Cache(limits));
        _ = budget.GetSnapshot();
    }

    private static void ReserveInvalidRequest(CacheMemoryBudget budget, long bytes, int entries)
    {
        var admitted = budget.TryReserve(bytes, entries, out var reservation);
        using var reservationLifetime = reservation;
        if (admitted || reservationLifetime is not null)
        {
            throw new InvalidOperationException("The invalid test reservation was unexpectedly admitted.");
        }
    }
}
