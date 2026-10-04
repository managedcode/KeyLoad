using KeyLoad.Replication;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests;

internal sealed class ReplicaTermMetadataLifetimeTests
{
    private const long EntryTerm = 1;
    private const long LogTerm = 2;
    private const long ReplicaIndex = 1;
    private const string BackupDirectoryName = "term-backup";
    private const string RestoredDirectoryName = "term-restored";

    [Test]
    public async Task LogDisposalRejectsAFormerlyWarmTermAndReopenStartsCold()
    {
        await ReplicaTermMetadataFixture.RunAsync(async fixture =>
        {
            ReplicaTermMetadataFixture.PrepareLog(fixture.Log, EntryTerm, LogTerm);
            await Assert.That(fixture.Log.TermAt(ReplicaIndex)).IsEqualTo(EntryTerm);
            var originalIdentity = fixture.Store.Identity;
            var backup = Path.Combine(fixture.DirectoryPath, BackupDirectoryName);
            var restoredDirectory = Path.Combine(fixture.DirectoryPath, RestoredDirectoryName);
            await Assert.That(fixture.Store.CreateBackup(backup)).IsEqualTo(fixture.Store.Position);
            var restoredIdentity = ZoneTreeStore.Restore(backup, restoredDirectory, originalIdentity.Incarnation);
            await Assert.That(restoredIdentity.NodeId == originalIdentity.NodeId).IsFalse();
            await Assert.That(restoredIdentity.Incarnation).IsEqualTo(originalIdentity.Incarnation);
            var reopenedStore = fixture.Own(new ZoneTreeStore(new ZoneTreeStoreOptions(restoredDirectory)
            {
                Incarnation = originalIdentity.Incarnation
            }));
            var reopenedLog = fixture.Own(new DurableReplicaLog(reopenedStore, fixture.Configuration with { Directory = restoredDirectory }));
            var reopenedBefore = reopenedStore.GetReadDiagnostics();
            await Assert.That(reopenedLog.TermAt(ReplicaIndex)).IsEqualTo(EntryTerm);
            var reopenedAfter = reopenedStore.GetReadDiagnostics();
            await Assert.That(reopenedAfter.BorrowedPointLookups - reopenedBefore.BorrowedPointLookups).IsEqualTo(1);

            var beforeProviderClose = fixture.Store.GetReadDiagnostics();
            fixture.CloseStore();
            await Assert.That(Assert.ThrowsExactly<ObjectDisposedException>(() => fixture.Log.TermAt(ReplicaIndex)))
                .IsNotNull();
            var afterProviderClose = fixture.Store.GetReadDiagnostics();
            await Assert.That(afterProviderClose.BorrowedPointLookups).IsEqualTo(beforeProviderClose.BorrowedPointLookups);
            fixture.CloseLog();

            await Assert.That(Assert.ThrowsExactly<ObjectDisposedException>(() => fixture.Log.TermAt(ReplicaIndex)))
                .IsNotNull();
        });
    }
}
