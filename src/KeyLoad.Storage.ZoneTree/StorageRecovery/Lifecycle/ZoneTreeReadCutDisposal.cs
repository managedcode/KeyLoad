using ZoneTree;

namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeReadCutDisposal
{
    private const string IteratorOwnershipChangedMessage = "The native read-cut iterator owner changed during disposal.";
    internal static bool SettleNative(ZoneTreeReadCutLease lease, Task capture, Task? traversal,
        List<Exception> failures)
    {
        ZoneTreeReadCutCleanup.Capture(lease.CancelLifetime, failures);
        ZoneTreeReadCutCleanup.Capture(() => capture.GetAwaiter().GetResult(), failures);
        if (traversal is not null)
        {
            ZoneTreeReadCutCleanup.Capture(() => traversal.GetAwaiter().GetResult(), failures);
        }

        var iterator = lease.CurrentIterator();
        return iterator is null || TryDispose(lease, iterator, failures);
    }

    private static bool TryDispose(ZoneTreeReadCutLease lease,
        IZoneTreeIterator<Memory<byte>, Memory<byte>> iterator, List<Exception> failures)
    {
        var before = failures.Count;
        ZoneTreeReadCutCleanup.Capture(iterator.Dispose, failures);
        if (failures.Count != before)
        {
            return false;
        }
        if (!lease.MarkIteratorDisposed(iterator))
        {
            failures.Add(new InvalidOperationException(IteratorOwnershipChangedMessage));
            return false;
        }
        return true;
    }

    internal static bool TryCleanup(Action action, List<Exception> failures)
    {
        var before = failures.Count;
        ZoneTreeReadCutCleanup.Capture(action, failures);
        return failures.Count == before;
    }
}
