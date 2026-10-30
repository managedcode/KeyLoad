namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeReadCutDisposal
{
    private const string SnapshotOwnershipChangedMessage = "The execution read-cut snapshot owner changed during disposal.";
    internal static bool RuntimeSettle(ZoneTreeReadCutLease lease, Task capture, Task? traversal,
        List<Exception> failures)
    {
        ZoneTreeReadCutCleanup.Capture(lease.CancelLifetime, failures);
        ZoneTreeReadCutCleanup.Capture(() => capture.GetAwaiter().GetResult(), failures);
        if (traversal is not null)
        {
            ZoneTreeReadCutCleanup.Capture(() => traversal.GetAwaiter().GetResult(), failures);
        }

        var snapshot = lease.CurrentSnapshot();
        return snapshot is null || TryDispose(lease, snapshot, failures);
    }

    private static bool TryDispose(ZoneTreeReadCutLease lease,
        ZoneTreeSnapshotCursor snapshot, List<Exception> failures)
    {
        var before = failures.Count;
        ZoneTreeReadCutCleanup.Capture(snapshot.Dispose, failures);
        if (failures.Count != before)
        {
            return false;
        }
        if (!lease.MarkSnapshotDisposed(snapshot))
        {
            failures.Add(new InvalidOperationException(SnapshotOwnershipChangedMessage));
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
