using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class CurrentSnapshotFenceContinuation
{
    internal static async Task CompleteAsync(string root, StoreIdentity identity, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using (var store = Open(root))
        {
            await CurrentSnapshotFenceAssertions.RequireIdentityAsync(identity, store.Identity);
            await Assert.That(store.Position).IsEqualTo(2L);
            await CurrentSnapshotFenceAssertions.RequireRowsAsync(store, installed: true, healthy: false);
            token.ThrowIfCancellationRequested();
            var position = store.Commit((transaction, committed) =>
            {
                transaction.PutRecord(KeyCodec.Encode("followup"), "current-fence");
                return committed;
            });
            await Assert.That(position).IsEqualTo(3L);
            await CurrentSnapshotFenceAssertions.RequireRowsAsync(store, installed: true, healthy: true);
        }
        await RequireColdAsync(root, identity, token);
        await RequireColdAsync(root, identity, token);
    }

    private static ZoneTreeStore Open(string root) => new(new(root),
        UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());

    private static async Task RequireColdAsync(string root, StoreIdentity identity, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var store = Open(root);
        await CurrentSnapshotFenceAssertions.RequireIdentityAsync(identity, store.Identity);
        await Assert.That(store.Position).IsEqualTo(3L);
        await CurrentSnapshotFenceAssertions.RequireRowsAsync(store, installed: true, healthy: true);
    }
}
