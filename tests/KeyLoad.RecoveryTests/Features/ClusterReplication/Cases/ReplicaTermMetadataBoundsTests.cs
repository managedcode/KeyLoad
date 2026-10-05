using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests;

internal sealed class ReplicaTermMetadataBoundsTests
{
    private const string SnapshotDirectoryName = "verified-snapshot-source";
    private const long SnapshotIndex = 2;
    private const long SnapshotTerm = 2;

    [Test]
    public async Task ZeroInvalidAndSnapshotTermsKeepExistingBoundaries()
    {
        await ReplicaTermMetadataFixture.RunAsync(async fixture =>
        {
            await Assert.That(fixture.Log.TermAt(0)).IsEqualTo(0);
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Log.TermAt(-1)).Code)
                .IsEqualTo(ErrorCode.NotFound);
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Log.TermAt(1)).Code)
                .IsEqualTo(ErrorCode.NotFound);

            var sourceDirectory = Path.Combine(fixture.DirectoryPath, SnapshotDirectoryName);
            var sourceStore = fixture.Own(new ZoneTreeStore(new ZoneTreeStoreOptions(sourceDirectory)
            {
                Incarnation = fixture.Configuration.Incarnation
            }, RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution()));
            var snapshot = ReplicaTermMetadataFixture.CreateVerifiedSnapshot(fixture, sourceStore,
                fixture.Configuration with { Directory = sourceDirectory });
            fixture.Log.PublishSnapshot(snapshot);
            await Assert.That(fixture.Log.TermAt(SnapshotIndex)).IsEqualTo(SnapshotTerm);
            await Assert.That(fixture.Log.TermAt(0)).IsEqualTo(0);
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Log.TermAt(1)).Code)
                .IsEqualTo(ErrorCode.NotFound);
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Log.TermAt(3)).Code)
                .IsEqualTo(ErrorCode.NotFound);
            await Assert.That(fixture.Store.GetReadDiagnostics().BorrowedPointLookups).IsEqualTo(0);
        });
    }

    [Test]
    public async Task InvalidSnapshotTermIsRejectedWithoutChangingTheCut()
    {
        await ReplicaTermMetadataFixture.RunAsync(async fixture =>
        {
            var sourceDirectory = Path.Combine(fixture.DirectoryPath, SnapshotDirectoryName);
            var sourceStore = fixture.Own(new ZoneTreeStore(new ZoneTreeStoreOptions(sourceDirectory)
            {
                Incarnation = fixture.Configuration.Incarnation
            }, RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution()));
            var verified = ReplicaTermMetadataFixture.CreateVerifiedSnapshot(fixture, sourceStore,
                fixture.Configuration with { Directory = sourceDirectory });
            var invalid = verified with { Term = 0 };

            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Log.PublishSnapshot(invalid)).Code)
                .IsEqualTo(ErrorCode.Validation);
            await Assert.That(fixture.Log.State.Snapshot).IsNull();
            await Assert.That(fixture.Log.TermAt(0)).IsEqualTo(0);
        });
    }
}
