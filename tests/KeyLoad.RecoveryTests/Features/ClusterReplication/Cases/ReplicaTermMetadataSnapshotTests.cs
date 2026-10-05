using KeyLoad.Replication;
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
        await ReplicaTermMetadataFixture.RunAsync(async fixture =>
        {
            var sourceDirectory = Path.Combine(fixture.DirectoryPath, SourceDirectoryName);
            var sourceStore = fixture.Own(new ZoneTreeStore(new ZoneTreeStoreOptions(sourceDirectory)
            {
                Incarnation = fixture.Configuration.Incarnation
            }, RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution()));
            var sourceLog = fixture.Own(new DurableReplicaLog(sourceStore, RecoveryExecutionOptions.Configuration(fixture.Configuration with { Directory = sourceDirectory })));
            ReplicaTermMetadataFixture.PrepareLog(fixture.Log, OldEntryTerm, LogTerm);
            ReplicaTermMetadataFixture.PrepareLog(sourceLog, NewEntryTerm, LogTerm);
            ReplicaTermMetadataFixture.WriteAppliedCut(fixture.Store, AppliedCut);
            ReplicaTermMetadataFixture.WriteAppliedCut(sourceStore, AppliedCut);
            await Assert.That(fixture.Store.Position).IsEqualTo(sourceStore.Position);
            await Assert.That(fixture.Log.State.Term).IsEqualTo(LogTerm);
            await Assert.That(OldEntryTerm <= fixture.Log.State.Term).IsTrue();
            await Assert.That(NewEntryTerm <= fixture.Log.State.Term).IsTrue();
            await Assert.That(fixture.Log.TermAt(ReplicaIndex)).IsEqualTo(OldEntryTerm);
            var oldIdentity = fixture.Store.Identity;
            var oldPosition = fixture.Store.Position;
            var beforeInstall = fixture.Store.GetReadDiagnostics();

            var snapshotPath = Path.Combine(fixture.DirectoryPath, SnapshotFileName);
            sourceStore.CreateSnapshot(snapshotPath, AppliedCut);
            fixture.Store.InstallSnapshot(snapshotPath, AppliedCut);

            await Assert.That(fixture.Store.Identity.ReadGeneration).IsEqualTo(oldIdentity.ReadGeneration + 1);
            await Assert.That(fixture.Store.Identity.NodeId).IsEqualTo(oldIdentity.NodeId);
            await Assert.That(fixture.Store.Identity.Incarnation).IsEqualTo(oldIdentity.Incarnation);
            await Assert.That(fixture.Store.Position).IsEqualTo(oldPosition);
            await Assert.That(fixture.Log.TermAt(ReplicaIndex)).IsEqualTo(NewEntryTerm);
            var afterInstall = fixture.Store.GetReadDiagnostics();
            await Assert.That(afterInstall.BorrowedPointLookups - beforeInstall.BorrowedPointLookups).IsEqualTo(1);
        });
    }

    [Test]
    public async Task SameCutCompactionPreservesTheWarmEntryObservation()
    {
        await ReplicaTermMetadataFixture.RunAsync(async fixture =>
        {
            ReplicaTermMetadataFixture.PrepareLog(fixture.Log, OldEntryTerm, LogTerm);
            await Assert.That(fixture.Log.TermAt(ReplicaIndex)).IsEqualTo(OldEntryTerm);
            var identity = fixture.Store.Identity;
            var position = fixture.Store.Position;

            var checkpoint = fixture.Store.Compact();

            await Assert.That(checkpoint.Position).IsEqualTo(position);
            await Assert.That(fixture.Store.Position).IsEqualTo(position);
            await Assert.That(fixture.Store.Identity.ReadGeneration).IsEqualTo(identity.ReadGeneration);
            await Assert.That(fixture.Store.Identity.NodeId).IsEqualTo(identity.NodeId);
            await Assert.That(fixture.Store.Identity.Incarnation).IsEqualTo(identity.Incarnation);
            var afterCompact = fixture.Store.GetReadDiagnostics();
            await Assert.That(fixture.Log.TermAt(ReplicaIndex)).IsEqualTo(OldEntryTerm);
            var afterTerm = fixture.Store.GetReadDiagnostics();
            await Assert.That(afterTerm.BorrowedPointLookups - afterCompact.BorrowedPointLookups).IsEqualTo(0);
            await Assert.That(afterTerm.PointExaminedBytes - afterCompact.PointExaminedBytes).IsEqualTo(0);
        });
    }

    [Test]
    public async Task WrongAppliedPositionRejectsVerifiedSnapshotWithoutChangingTheWarmCut()
    {
        await ReplicaTermMetadataFixture.RunAsync(async fixture =>
        {
            ReplicaTermMetadataFixture.PrepareLog(fixture.Log, OldEntryTerm, LogTerm);
            ReplicaTermMetadataFixture.WriteAppliedCut(fixture.Store, AppliedCut);
            await Assert.That(fixture.Log.TermAt(ReplicaIndex)).IsEqualTo(OldEntryTerm);
            var identity = fixture.Store.Identity;
            var position = fixture.Store.Position;
            var before = fixture.Store.GetReadDiagnostics();
            var sourceDirectory = Path.Combine(fixture.DirectoryPath, SourceDirectoryName);
            var sourceStore = fixture.Own(new ZoneTreeStore(new ZoneTreeStoreOptions(sourceDirectory)
            {
                Incarnation = fixture.Configuration.Incarnation
            }, RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution()));
            var verified = ReplicaTermMetadataFixture.CreateVerifiedSnapshot(fixture, sourceStore,
                fixture.Configuration with { Directory = sourceDirectory });
            var imagePath = Path.Combine(sourceDirectory, ReplicaProtocol.SnapshotDirectory, verified.FileName);

            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Store.InstallSnapshot(imagePath, AppliedCut)).Code)
                .IsEqualTo(ErrorCode.TokenInvalidated);
            await Assert.That(fixture.Store.Position).IsEqualTo(position);
            await Assert.That(fixture.Store.Identity.ReadGeneration).IsEqualTo(identity.ReadGeneration);
            await Assert.That(fixture.Store.Identity.NodeId).IsEqualTo(identity.NodeId);
            await Assert.That(fixture.Store.Identity.Incarnation).IsEqualTo(identity.Incarnation);
            await Assert.That(fixture.Log.TermAt(ReplicaIndex)).IsEqualTo(OldEntryTerm);
            var after = fixture.Store.GetReadDiagnostics();
            await Assert.That(after.BorrowedPointLookups - before.BorrowedPointLookups).IsEqualTo(0);
        });
    }
}
