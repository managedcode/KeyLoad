using System.Text;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.Storage.ZoneTree.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class ZoneTreeCoordinatedPointCacheDisposalEntryTests
{
    private const string StagedKeyText = "cache/coordinated/disposal-entry-staged";
    private static readonly byte[] Key = "cache/coordinated/disposal-entry"u8.ToArray();
    private static readonly byte[] Value = [17, 34, 68, 136];
    private static readonly byte[] StagedValue = [1, 3, 5];
    private static readonly byte[] StagedKeyBytes = Encoding.UTF8.GetBytes(StagedKeyText);

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CallbackDisposeRejectsBeforeClosingAndSameDirectoryReopens(bool fromCommit)
    {
        using var fixture = new ZoneTreeCoordinatedPointCacheFileFixture();
        await fixture.RunAsync(() => VerifyCallbackDisposeAsync(fixture, fromCommit));
    }

    private static async Task VerifyCallbackDisposeAsync(ZoneTreeCoordinatedPointCacheFileFixture fixture,
        bool fromCommit)
    {
        using var permit = ZoneTreeCoordinatedPointCacheTestSupport.CreateAcceptedPermit(1, out _, out var receipt);
        var store = fixture.OpenStore();
        ZoneTreeCoordinatedPointCacheTestSupport.Put(store, Key, Value);
        var control = ZoneTreeCoordinatedPointCacheTestSupport.CreateControl(store, fixture, permit);
        await Assert.That(control.TryApply(receipt)).IsEqualTo(ZoneTreePointCacheControlResult.Applied);
        var initial = store.Read(view => view.ReadOwnedValue(Key));
        var beforeRejectedDispose = control.GetDiagnostics();

        _ = Assert.ThrowsExactly<LockRecursionException>(() => DisposeFromActualCallback(store, fromCommit));

        var afterRejectedDispose = control.GetDiagnostics();
        var receiptCurrent = permit.IsCurrentAcceptance(receipt);
        var sameReceipt = control.TryApply(receipt);
        var warm = store.Read(view => view.ReadOwnedValue(Key));
        var afterWarm = control.GetDiagnostics();
        await AssertUnchangedAfterRejectedDisposeAsync(beforeRejectedDispose, afterRejectedDispose,
            receiptCurrent, sameReceipt, initial, warm, afterWarm);

        store.Dispose();
        await AssertClosedReleaseAndReopenedBytesAsync(fixture, store, control);
    }

    private static async Task AssertClosedReleaseAndReopenedBytesAsync(
        ZoneTreeCoordinatedPointCacheFileFixture fixture, ZoneTreeStore store,
        ZoneTreePointCacheControl control)
    {
        var closed = control.GetDiagnostics();
        var released = fixture.Budget.GetSnapshot();
        var reopened = fixture.ReopenStoreAtSameDirectory(store);
        var reopenedValue = reopened.Read(view => view.ReadOwnedValue(Key));
        var uncommittedValue = reopened.Read(view => view.ReadOwnedValue(StagedKeyBytes));
        var reopenedCache = reopened.GetPointCacheDiagnostics();
        var reopenedReads = reopened.GetReadDiagnostics();

        await Assert.That(closed.Closed).IsTrue();
        await Assert.That(closed.Enabled).IsFalse();
        await Assert.That(released.RetainedBytes).IsEqualTo(0L);
        await Assert.That(released.RetainedEntries).IsEqualTo(0);
        await Assert.That(reopenedValue).IsNotNull();
        await Assert.That(reopenedValue!.SequenceEqual(Value)).IsTrue();
        await Assert.That(uncommittedValue).IsNull();
        await Assert.That(reopenedCache.Configured).IsFalse();
        await Assert.That(reopenedReads.OwnedPointLookups).IsEqualTo(2L);
    }

    private static void DisposeFromActualCallback(ZoneTreeStore store, bool fromCommit)
    {
        if (fromCommit)
        {
            store.Commit((transaction, _) =>
            {
                transaction.Put(StagedKeyBytes, StagedValue);
                store.Dispose();
                return true;
            });
            return;
        }

        _ = store.Read(_ =>
        {
            store.Dispose();
            return true;
        });
    }

    private static async Task AssertUnchangedAfterRejectedDisposeAsync(ZoneTreePointCacheSnapshot before,
        ZoneTreePointCacheSnapshot after, bool receiptCurrent, ZoneTreePointCacheControlResult sameReceipt,
        byte[]? initial, byte[]? warm, ZoneTreePointCacheSnapshot afterWarm)
    {
        await Assert.That(receiptCurrent).IsTrue();
        await Assert.That(sameReceipt).IsEqualTo(ZoneTreePointCacheControlResult.AlreadyApplied);
        await Assert.That(after.Closed).IsFalse();
        await Assert.That(after.Enabled).IsTrue();
        await Assert.That(after.RetainedBytes).IsEqualTo(before.RetainedBytes);
        await Assert.That(after.ChargedEntries).IsEqualTo(before.ChargedEntries);
        await Assert.That(after.LiveEntries).IsEqualTo(before.LiveEntries);
        await Assert.That(after.RetiredPinnedEntries).IsEqualTo(before.RetiredPinnedEntries);
        await Assert.That(after.InFlightEntries).IsEqualTo(before.InFlightEntries);
        await Assert.That(after.ActivePins).IsEqualTo(before.ActivePins);
        await Assert.That(after.RetainedBytes).IsGreaterThan(0L);
        await Assert.That(after.Hits).IsEqualTo(before.Hits);
        await Assert.That(after.Misses).IsEqualTo(before.Misses);
        await Assert.That(after.NativeLookups).IsEqualTo(before.NativeLookups);
        await Assert.That(after.Admissions).IsEqualTo(before.Admissions);
        await Assert.That(after.Evictions).IsEqualTo(before.Evictions);
        await Assert.That(initial).IsNotNull();
        await Assert.That(initial!.SequenceEqual(Value)).IsTrue();
        await Assert.That(warm).IsNotNull();
        await Assert.That(warm!.SequenceEqual(Value)).IsTrue();
        await Assert.That(afterWarm.Hits).IsEqualTo(before.Hits + 1);
        await Assert.That(afterWarm.NativeLookups).IsEqualTo(before.NativeLookups);
        await Assert.That(afterWarm.RetainedBytes).IsEqualTo(before.RetainedBytes);
    }
}
