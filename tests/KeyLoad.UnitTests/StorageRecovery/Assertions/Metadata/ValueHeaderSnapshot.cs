using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.StorageRecovery;

internal static class ValueHeaderSnapshot
{
    internal static async Task HealthyAsync(ZoneTreeStore store, ZoneTreeExistingStoreFixture files)
    {
        var path = Path.Combine(files.DirectoryPath, ValueHeaderRecords.SnapshotFile);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var snapshot = store.CreateSnapshot(path);
            var verified = store.VerifySnapshot(path);
            await Assert.That(snapshot.Incarnation).IsEqualTo(files.Identity.Incarnation);
            await Assert.That(snapshot.Position).IsEqualTo(ValueHeaderRecords.HealthyPosition);
            await Assert.That(snapshot.RecordCount).IsEqualTo((long)ValueHeaderRecords.CompleteRecords);
            await Assert.That(snapshot.AppliedPosition).IsEqualTo(NoAppliedPosition);
            await Assert.That(verified).IsEqualTo(snapshot);
        }, failures);
        ServerFailureObserver.Observe(() => File.Delete(path), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private const long NoAppliedPosition = 0;

    internal static async Task RequireAsync(ZoneTreeStore store, ZoneTreeExistingStoreFixture files)
    {
        var path = Path.Combine(files.DirectoryPath, ValueHeaderRecords.SnapshotFile);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var refused = Assert.ThrowsExactly<KeyLoadException>(() => store.CreateSnapshot(path));
            await Assert.That(refused.Code).IsEqualTo(ErrorCode.Corruption);
            var incomplete = Assert.ThrowsExactly<KeyLoadException>(() => store.VerifySnapshot(path));
            await Assert.That(incomplete.Code).IsEqualTo(ErrorCode.Corruption);
        }, failures);
        ServerFailureObserver.Observe(() => File.Delete(path), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
