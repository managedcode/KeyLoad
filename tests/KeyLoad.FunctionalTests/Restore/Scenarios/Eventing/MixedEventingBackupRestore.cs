using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.FunctionalTests.BackupRestore;

internal static class MixedEventingBackupRestore
{
    internal static async Task<(StoreIdentity Identity, long Position)> CaptureAsync(TestDatabase source, string backup,
        CancellationToken token)
    {
        var identity = source.Store.Identity;
        var position = await source.Store.CreateBackupAsync(backup, token);
        await Assert.That(position).IsEqualTo(source.Store.Position);
        return (identity, position);
    }

    internal static async Task RestoreAsync(TestDatabase source, (StoreIdentity Identity, long Position) capture,
        string backup, string destination, CancellationToken token)
    {
        await Assert.That(capture.Identity.NodeId).IsEqualTo(source.Store.Identity.NodeId);
        var identity = await ZoneTreeStore.RestoreAsync(backup, destination, source.StorageExecution, cancellationToken: token);
        await Assert.That(identity.Incarnation).IsNotEqualTo(capture.Identity.Incarnation);
        await Assert.That(identity.NodeId).IsNotEqualTo(capture.Identity.NodeId);
        await Assert.That(identity.SigningKey.Span.SequenceEqual(capture.Identity.SigningKey.Span)).IsFalse();
        await Assert.That(identity.DispatchPaused).IsTrue();
    }
}
