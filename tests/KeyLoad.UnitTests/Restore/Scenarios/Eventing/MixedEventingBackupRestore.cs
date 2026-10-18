using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class MixedEventingBackupRestore
{
    private const int RestoreAuthorityCommits = 1;

    internal static (StoreIdentity Identity, long Position) Capture(TestDatabase source, string backup, CancellationToken token)
    {
        source.Store.CreateBackup(backup, token);
        return ZoneTreeStore.VerifyBackup(backup, source.StorageExecution, token);
    }

    internal static async Task<long> RestoreAsync(TestDatabase source, (StoreIdentity Identity, long Position) capture,
        string unpacked, string destination, CancellationToken token)
    {
        await Assert.That(capture.Identity.NodeId).IsEqualTo(source.Store.Identity.NodeId);
        var identity = ZoneTreeStore.Restore(unpacked, destination, source.StorageExecution, cancellationToken: token);
        await Assert.That(identity.Incarnation).IsNotEqualTo(capture.Identity.Incarnation);
        await Assert.That(identity.NodeId).IsNotEqualTo(capture.Identity.NodeId);
        await Assert.That(identity.SigningKey.Span.SequenceEqual(capture.Identity.SigningKey.Span)).IsFalse();
        await Assert.That(identity.DispatchPaused).IsTrue();
        return checked(capture.Position + RestoreAuthorityCommits);
    }
}
