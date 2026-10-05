using KeyLoad.Replication;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests;

internal sealed class ReplicaTermMetadataFailureTests
{
    private const string FaultKey = "term-failure";
    private const string FaultValue = "fault-trigger";
    private const string SourceDirectoryName = "fault-snapshot-source";
    private const string SnapshotFileName = "fault-snapshot.snapshot";
    private const string InjectedSnapshotInstallFailure = "Injected native snapshot installation fault.";
    private const long ReplicaIndex = 1;
    private const long EntryTerm = 1;
    private const long RejectedEntryTerm = 2;
    private const long LogTerm = 2;
    private const long AppliedCut = 1;

    [Test]
    public async Task ProviderPublicationPoisonCannotServeAPreviouslyWarmTerm()
    {
        var failPublication = false;
        await ReplicaTermMetadataFixture.RunAsync(async fixture =>
        {
            ReplicaTermMetadataFixture.PrepareLog(fixture.Log, EntryTerm, LogTerm);
            await Assert.That(fixture.Log.TermAt(ReplicaIndex)).IsEqualTo(EntryTerm);
            var beforeFault = fixture.Store.GetReadDiagnostics();
            var oldIdentity = fixture.Store.Identity;
            var oldPosition = fixture.Store.Position;
            failPublication = true;

            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Store.Commit((transaction, _) =>
            {
                transaction.PutRecord(KeyCodec.Encode(FaultKey), FaultValue);
                return true;
            })).Code).IsEqualTo(ErrorCode.UnknownWriteOutcome);
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Log.TermAt(ReplicaIndex)).Code)
                .IsEqualTo(ErrorCode.RecoveryRequired);
            await Assert.That(fixture.Store.Position).IsEqualTo(oldPosition);
            await Assert.That(fixture.Store.Identity.ReadGeneration).IsEqualTo(oldIdentity.ReadGeneration);
            await Assert.That(fixture.Store.Identity.NodeId).IsEqualTo(oldIdentity.NodeId);
            await Assert.That(fixture.Store.Identity.Incarnation).IsEqualTo(oldIdentity.Incarnation);
            var afterRejectedRead = fixture.Store.GetReadDiagnostics();
            await Assert.That(afterRejectedRead.BorrowedPointLookups).IsEqualTo(beforeFault.BorrowedPointLookups);
        }, (stage, _, _) =>
        {
            if (failPublication && stage == CommitStage.JournalFlushed)
            {
                throw new IOException("Injected native journal publication fault.");
            }
        });
    }

    [Test]
    public async Task PrepublicationKeyValidationKeepsTheOldCommittedCutHealthy()
    {
        await ReplicaTermMetadataFixture.RunAsync(async fixture =>
        {
            ReplicaTermMetadataFixture.PrepareLog(fixture.Log, EntryTerm, LogTerm);
            await Assert.That(fixture.Log.TermAt(ReplicaIndex)).IsEqualTo(EntryTerm);
            var identity = fixture.Store.Identity;
            var position = fixture.Store.Position;
            var before = fixture.Store.GetReadDiagnostics();

            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Store.Commit((transaction, _) =>
            {
                transaction.Put(ReplicaProtocol.EntryStorageKey(ReplicaIndex),
                    ReplicaProtocolCodec.Serialize(new ReplicaEntry(ReplicaIndex, RejectedEntryTerm, null)));
                transaction.Put([], [0x01]);
                return true;
            })).Code).IsEqualTo(ErrorCode.Corruption);
            await Assert.That(fixture.Store.Position).IsEqualTo(position);
            await Assert.That(fixture.Store.Identity.ReadGeneration).IsEqualTo(identity.ReadGeneration);
            await Assert.That(fixture.Store.Identity.NodeId).IsEqualTo(identity.NodeId);
            await Assert.That(fixture.Store.Identity.Incarnation).IsEqualTo(identity.Incarnation);
            await Assert.That(fixture.Log.TermAt(ReplicaIndex)).IsEqualTo(EntryTerm);
            var after = fixture.Store.GetReadDiagnostics();
            await Assert.That(after.BorrowedPointLookups - before.BorrowedPointLookups).IsEqualTo(0);
        });
    }

    [Test]
    public Task FailedPreparedSnapshotInstallationPoisonsAndRetiresHandles()
        => AssertFailedSnapshotInstallationPoisonsAndRetiresHandles(CommitStage.InstallPrepared);

    [Test]
    public Task FailedJournalSwappedSnapshotInstallationPoisonsAndRetiresHandles()
        => AssertFailedSnapshotInstallationPoisonsAndRetiresHandles(CommitStage.JournalSwapped);

    private static async Task AssertFailedSnapshotInstallationPoisonsAndRetiresHandles(CommitStage failedStage)
    {
        var failInstall = false;
        var faultObserved = false;
        await ReplicaTermMetadataFixture.RunAsync(async fixture =>
        {
            var sourceDirectory = Path.Combine(fixture.DirectoryPath, SourceDirectoryName);
            var sourceStore = fixture.Own(new ZoneTreeStore(new ZoneTreeStoreOptions(sourceDirectory)
            {
                Incarnation = fixture.Configuration.Incarnation
            }));
            var sourceLog = fixture.Own(new DurableReplicaLog(sourceStore, RecoveryExecutionOptions.Configuration(fixture.Configuration with { Directory = sourceDirectory })));
            ReplicaTermMetadataFixture.PrepareLog(fixture.Log, EntryTerm, LogTerm);
            ReplicaTermMetadataFixture.PrepareLog(sourceLog, EntryTerm, LogTerm);
            ReplicaTermMetadataFixture.WriteAppliedCut(fixture.Store, AppliedCut);
            ReplicaTermMetadataFixture.WriteAppliedCut(sourceStore, AppliedCut);
            var snapshotPath = Path.Combine(fixture.DirectoryPath, SnapshotFileName);
            sourceStore.CreateSnapshot(snapshotPath, AppliedCut);
            await Assert.That(fixture.Log.TermAt(ReplicaIndex)).IsEqualTo(EntryTerm);
            var beforeFault = fixture.Store.GetReadDiagnostics();
            failInstall = true;

            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Store.InstallSnapshot(snapshotPath, AppliedCut)).Code)
                .IsEqualTo(ErrorCode.RecoveryRequired);
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Log.TermAt(ReplicaIndex)).Code)
                .IsEqualTo(ErrorCode.RecoveryRequired);
            var afterRejectedRead = fixture.Store.GetReadDiagnostics();
            await Assert.That(afterRejectedRead.BorrowedPointLookups).IsEqualTo(beforeFault.BorrowedPointLookups);

            fixture.CloseLog();
            fixture.CloseStore();
            fixture.Store.Dispose();
            fixture.Store.Dispose();
        }, (stage, _, _) =>
        {
            if (failInstall && stage == failedStage)
            {
                faultObserved = true;
                throw new IOException(InjectedSnapshotInstallFailure);
            }
        });
        await Assert.That(faultObserved).IsTrue();
    }
}
