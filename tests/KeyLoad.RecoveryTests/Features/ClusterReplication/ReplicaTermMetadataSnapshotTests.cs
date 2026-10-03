using KeyLoad.Replication;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests;

internal sealed class ReplicaTermMetadataSnapshotTests
{
    private const string SourceDirectoryName = "snapshot-source";
    private const string SnapshotFileName = "same-position.snapshot";
    private const long AppliedCut = 1;
    private const long ReplicaIndex = 1;
    private const long LogTerm = 2;
    private const long OldEntryTerm = 1;
    private const long NewEntryTerm = 2;

    [Test]
    public async Task SamePositionValidSnapshotReplacementInvalidatesTheOldEntryObservation()
    {
        using var fixture = new ReplicaTermMetadataFixture();
        var sourceDirectory = Path.Combine(fixture.DirectoryPath, SourceDirectoryName);
        using var sourceStore = new ZoneTreeStore(new ZoneTreeStoreOptions(sourceDirectory)
        {
            Incarnation = fixture.Configuration.Incarnation
        });
        using var sourceLog = new DurableReplicaLog(sourceStore, fixture.Configuration with { Directory = sourceDirectory });
        PrepareLog(fixture.Log, OldEntryTerm);
        PrepareLog(sourceLog, NewEntryTerm);
        WriteAppliedCut(fixture.Store);
        WriteAppliedCut(sourceStore);
        await Assert.That(fixture.Store.Position).IsEqualTo(sourceStore.Position);
        await Assert.That(fixture.Log.TermAt(ReplicaIndex)).IsEqualTo(OldEntryTerm);
        var oldGeneration = fixture.Store.Identity.ReadGeneration;
        var oldPosition = fixture.Store.Position;
        var beforeInstall = fixture.Store.GetReadDiagnostics();

        var snapshotPath = Path.Combine(fixture.DirectoryPath, SnapshotFileName);
        sourceStore.CreateSnapshot(snapshotPath, AppliedCut);
        fixture.Store.InstallSnapshot(snapshotPath, AppliedCut);

        await Assert.That(fixture.Store.Identity.ReadGeneration).IsEqualTo(oldGeneration + 1);
        await Assert.That(fixture.Store.Position).IsEqualTo(oldPosition);
        await Assert.That(fixture.Log.TermAt(ReplicaIndex)).IsEqualTo(NewEntryTerm);
        var afterInstall = fixture.Store.GetReadDiagnostics();
        await Assert.That(afterInstall.BorrowedPointLookups - beforeInstall.BorrowedPointLookups).IsEqualTo(1);
    }

    private static void PrepareLog(DurableReplicaLog log, long entryTerm)
    {
        log.SaveTermAndVote(LogTerm, null);
        log.Append([new(ReplicaIndex, entryTerm, null)]);
        log.Commit(ReplicaIndex);
    }

    private static void WriteAppliedCut(ZoneTreeStore store)
        => store.Commit((transaction, _) =>
        {
            transaction.PutRecord(KeyCodec.Encode(ReplicaTermMetadataFixture.SystemNamespace,
                ReplicaTermMetadataFixture.AppliedPositionKey), AppliedCut);
            return true;
        });
}
