using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.Storage.ZoneTree.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class ZoneTreeCoordinatedPointCacheLifetimeTests
{
    private const string BorrowFailure = "The borrowed value changed after retirement.";
    private static readonly byte[] Key = "cache/coordinated/pinned"u8.ToArray();
    private static readonly byte[] Value = [3, 6, 9, 12];
    private sealed class HeldOperations
    {
        internal Task<bool>? Borrower { get; set; }
        internal Task<bool>? Disposer { get; set; }
        internal byte[]? BorrowedCopy { get; set; }
    }

    [Test]
    public async Task RetiredPinStaysChargedUntilBorrowerChecksExactBytesAfterRelease()
    {
        using var fixture = new ZoneTreeCoordinatedPointCacheFileFixture();
        await fixture.RunAsync(async () =>
        {
            using var permit = ZoneTreeCoordinatedPointCacheTestSupport.CreateAcceptedPermit(1, out var grant, out var receipt);
            var (store, control) = await OpenWarmStoreAsync(fixture, permit, receipt);
            var beforeBorrow = control.GetDiagnostics();
            var borrowedCopy = await RetireHeldBorrowerAsync(store, permit, grant, receipt, control, beforeBorrow);
            await AssertRetiredPinReleasedAsync(control, fixture, borrowedCopy);
        });
    }

    [Test]
    public async Task StoreDisposeClosesAdmissionBeforeWaitingForPinnedBorrowerAndReleasesAfterJoin()
    {
        using var fixture = new ZoneTreeCoordinatedPointCacheFileFixture();
        await fixture.RunAsync(async () =>
        {
            using var permit = ZoneTreeCoordinatedPointCacheTestSupport.CreateAcceptedPermit(1, out _, out var receipt);
            var (store, control) = await OpenWarmStoreAsync(fixture, permit, receipt);
            var beforeBorrow = control.GetDiagnostics();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var failures = new List<Exception>();
            var operations = new HeldOperations();
            try
            {
                await ZoneTreeCoordinatedPointCacheTestSupport.CollectFailureAsync(async () =>
                {
                    await CloseStoreWhileBorrowedAsync(store, control, fixture, beforeBorrow, entered, release, operations);
                }, failures);
            }
            finally
            {
                release.TrySetResult();
                if (operations.Borrower is not null)
                {
                    await ZoneTreeCoordinatedPointCacheTestSupport.JoinAndCollectAsync(operations.Borrower, failures);
                }
                if (operations.Disposer is not null)
                {
                    await ZoneTreeCoordinatedPointCacheTestSupport.JoinAndCollectAsync(operations.Disposer, failures);
                }
            }

            ZoneTreeCoordinatedPointCacheTestSupport.ThrowFailures(failures);
            await Assert.That(operations.BorrowedCopy).IsNotNull();
            await Assert.That(operations.BorrowedCopy!.SequenceEqual(Value)).IsTrue();
            await Assert.That(control.GetDiagnostics().RetainedBytes).IsEqualTo(0L);
            await Assert.That(fixture.Budget.GetSnapshot().RetainedBytes).IsEqualTo(0L);
        });
    }

    private static async Task<(ZoneTreeStore Store, ZoneTreePointCacheControl Control)> OpenWarmStoreAsync(
        ZoneTreeCoordinatedPointCacheFileFixture fixture, CacheReadPermit permit,
        CacheReadPermitAcceptance receipt)
    {
        var store = fixture.OpenStore();
        ZoneTreeCoordinatedPointCacheTestSupport.Put(store, Key, Value);
        var control = ZoneTreeCoordinatedPointCacheTestSupport.CreateControl(store, fixture, permit);
        await Assert.That(control.TryApply(receipt)).IsEqualTo(ZoneTreePointCacheControlResult.Applied);
        _ = store.Read(view => view.ReadOwnedValue(Key));
        return (store, control);
    }

    private static async Task<byte[]?> RetireHeldBorrowerAsync(ZoneTreeStore store,
        CacheReadPermit permit, Guid grant, CacheReadPermitAcceptance receipt,
        ZoneTreePointCacheControl control, ZoneTreePointCacheSnapshot beforeBorrow)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failures = new List<Exception>();
        byte[]? borrowedCopy = null;
        Task<bool>? borrower = null;
        try
        {
            await ZoneTreeCoordinatedPointCacheTestSupport.CollectFailureAsync(async () =>
            {
                borrower = StartBorrow(store, entered, release, bytes => borrowedCopy = bytes);
                await VerifyRetiredPinAsync(entered, permit, grant, receipt, control, beforeBorrow);
            }, failures);
        }
        finally
        {
            release.TrySetResult();
            if (borrower is not null)
            {
                await ZoneTreeCoordinatedPointCacheTestSupport.JoinAndCollectAsync(borrower, failures);
            }
        }

        ZoneTreeCoordinatedPointCacheTestSupport.ThrowFailures(failures);
        return borrowedCopy;
    }

    private static Task<bool> StartBorrow(ZoneTreeStore store, TaskCompletionSource entered,
        TaskCompletionSource release, Action<byte[]> captured)
        => ZoneTreeCoordinatedPointCacheTestSupport.StartLongRunning(() => store.Read(view =>
            view.ReadValue(Key, value =>
            {
                entered.TrySetResult();
                release.Task.WaitAsync(ZoneTreeCoordinatedPointCacheTestSupport.WaitLimit, TimeProvider.System)
                    .GetAwaiter().GetResult();
                if (!value.SequenceEqual(Value))
                {
                    throw new InvalidOperationException(BorrowFailure);
                }
                captured(value.ToArray());
            })));

    private static async Task VerifyRetiredPinAsync(TaskCompletionSource entered, CacheReadPermit permit,
        Guid grant, CacheReadPermitAcceptance receipt, ZoneTreePointCacheControl control,
        ZoneTreePointCacheSnapshot beforeBorrow)
    {
        await entered.Task.WaitAsync(ZoneTreeCoordinatedPointCacheTestSupport.WaitLimit, TimeProvider.System);
        await Assert.That(permit.TryWithdraw(grant, receipt.Revision)).IsTrue();
        await Assert.That(control.Retire(receipt.Revision)).IsTrue();
        var retired = control.GetDiagnostics();
        await Assert.That(retired.ActivePins).IsEqualTo(1);
        await Assert.That(retired.RetiredPinnedEntries).IsEqualTo(1);
        await Assert.That(retired.RetainedBytes).IsEqualTo(beforeBorrow.RetainedBytes);
    }

    private static async Task AssertRetiredPinReleasedAsync(ZoneTreePointCacheControl control,
        ZoneTreeCoordinatedPointCacheFileFixture fixture, byte[]? borrowedCopy)
    {
        var snapshot = control.GetDiagnostics();
        await Assert.That(borrowedCopy).IsNotNull();
        await Assert.That(borrowedCopy!.SequenceEqual(Value)).IsTrue();
        await Assert.That(snapshot.ActivePins).IsEqualTo(0);
        await Assert.That(snapshot.RetiredPinnedEntries).IsEqualTo(0);
        await Assert.That(snapshot.RetainedBytes).IsEqualTo(fixture.CreateOptions().IndexChargeBytes);
    }

    private static async Task CloseStoreWhileBorrowedAsync(ZoneTreeStore store,
        ZoneTreePointCacheControl control, ZoneTreeCoordinatedPointCacheFileFixture fixture,
        ZoneTreePointCacheSnapshot beforeBorrow, TaskCompletionSource entered,
        TaskCompletionSource release, HeldOperations operations)
    {
        operations.Borrower = StartBorrow(store, entered, release, bytes => operations.BorrowedCopy = bytes);
        await entered.Task.WaitAsync(ZoneTreeCoordinatedPointCacheTestSupport.WaitLimit, TimeProvider.System);
        operations.Disposer = ZoneTreeCoordinatedPointCacheTestSupport.StartLongRunning(() =>
        {
            store.Dispose();
            return true;
        });
        await WaitForClosedAsync(control);
        var closing = control.GetDiagnostics();
        await Assert.That(operations.Disposer.IsCompleted).IsFalse();
        await Assert.That(closing.Closed).IsTrue();
        await Assert.That(closing.Enabled).IsFalse();
        await Assert.That(closing.ActivePins).IsEqualTo(1);
        await Assert.That(closing.RetainedBytes).IsEqualTo(beforeBorrow.RetainedBytes);
        await Assert.That(fixture.Budget.GetSnapshot().RetainedBytes).IsGreaterThan(0L);
    }

    private static async Task WaitForClosedAsync(ZoneTreePointCacheControl control)
    {
        using var deadline = new CancellationTokenSource(ZoneTreeCoordinatedPointCacheTestSupport.WaitLimit,
            TimeProvider.System);
        while (!control.GetDiagnostics().Closed)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), TimeProvider.System, deadline.Token);
        }
    }

}
