using KeyLoad.Replication;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests;

internal sealed class ReplicaTermMetadataLifetimeTests
{
    private const long EntryTerm = 1;
    private const long LogTerm = 2;
    private const long ReplicaIndex = 1;

    [Test]
    public async Task LogDisposalRejectsAFormerlyWarmTermAndReopenStartsCold()
    {
        await ReplicaTermMetadataFixture.RunAsync(async fixture =>
        {
            ReplicaTermMetadataFixture.PrepareLog(fixture.Log, EntryTerm, LogTerm);
            await Assert.That(fixture.Log.TermAt(ReplicaIndex)).IsEqualTo(EntryTerm);
            var originalIdentity = fixture.Store.Identity;
            var originalPosition = fixture.Store.Position;
            var beforeProviderClose = fixture.Store.GetReadDiagnostics();
            fixture.CloseStore();
            await Assert.That(Assert.ThrowsExactly<ObjectDisposedException>(() => fixture.Log.TermAt(ReplicaIndex)))
                .IsNotNull();
            var afterProviderClose = fixture.Store.GetReadDiagnostics();
            await Assert.That(afterProviderClose.BorrowedPointLookups).IsEqualTo(beforeProviderClose.BorrowedPointLookups);
            fixture.CloseLog();
            await Assert.That(Assert.ThrowsExactly<ObjectDisposedException>(() => fixture.Log.TermAt(ReplicaIndex)))
                .IsNotNull();

            var reopenedStore = fixture.Own(new ZoneTreeStore(new ZoneTreeStoreOptions(fixture.Configuration.Directory)
            {
                Incarnation = originalIdentity.Incarnation
            }, RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution()));
            await Assert.That(reopenedStore.Identity.NodeId).IsEqualTo(originalIdentity.NodeId);
            await Assert.That(reopenedStore.Identity.Incarnation).IsEqualTo(originalIdentity.Incarnation);
            await Assert.That(reopenedStore.Identity.SigningKey.Span.SequenceEqual(originalIdentity.SigningKey.Span)).IsTrue();
            await Assert.That(reopenedStore.Position).IsEqualTo(originalPosition);
            var reopenedLog = fixture.Own(new DurableReplicaLog(reopenedStore,
                RecoveryExecutionOptions.Configuration(fixture.Configuration)));
            var reopenedBefore = reopenedStore.GetReadDiagnostics();
            await Assert.That(reopenedLog.TermAt(ReplicaIndex)).IsEqualTo(EntryTerm);
            var reopenedAfter = reopenedStore.GetReadDiagnostics();
            await Assert.That(reopenedAfter.BorrowedPointLookups - reopenedBefore.BorrowedPointLookups).IsEqualTo(1);
        });
    }
}
