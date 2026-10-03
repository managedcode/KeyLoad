using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class EpochStoragePreservationTests
{
    private const int SourcePosition = 1;

    [Test]
    public async Task AcEpoch004CurrentMaintenanceInstallBackupAndRestoreRetainEpoch6()
    {
        using var fixture = new EpochStorageFixture();
        StorageSnapshot snapshot;
        using (var store = new ZoneTreeStore(fixture.SourceOptions))
        {
            store.Commit((transaction, position) =>
            {
                transaction.Put(fixture.FirstKey, fixture.FirstValue);
                transaction.Put(fixture.SecondKey, fixture.SecondValue);
                transaction.Put(KeyCodec.Encode("system", "last-applied"), NativeSerialization.Serialize(position));
                return position;
            });
            store.SetDispatchPaused(true);
            snapshot = store.CreateSnapshot(fixture.Snapshot);
            await AssertEpoch6Async(store.Identity);
            await AssertRejectedLegacyInstallPreservesSnapshotAsync(store, fixture, snapshot);

            store.Compact();
            await AssertEpoch6Async(store.Identity);
            var installed = store.InstallSnapshot(fixture.Snapshot, snapshot.AppliedPosition);
            await Assert.That(installed.Position).IsEqualTo(SourcePosition);
            await AssertEpoch6Async(store.Identity);
            await AssertValuesAsync(store, fixture);
            store.CreateBackup(fixture.Backup);
        }

        var restoredIdentity = ZoneTreeStore.Restore(fixture.Backup, fixture.Restored);
        await AssertEpoch6Async(restoredIdentity);
        using var restored = new ZoneTreeStore(new(fixture.Restored));
        await AssertEpoch6Async(restored.Identity);
        await AssertValuesAsync(restored, fixture);
    }

    private static async Task AssertEpoch6Async(StoreIdentity identity)
        => await Assert.That(identity.FormatVersion).IsEqualTo(EpochStorageFixture.CurrentEpoch);

    private static async Task AssertRejectedLegacyInstallPreservesSnapshotAsync(ZoneTreeStore store,
        EpochStorageFixture fixture, StorageSnapshot snapshot)
    {
        var snapshotBytes = await File.ReadAllBytesAsync(fixture.Snapshot);
        var identityPath = Path.Combine(fixture.Source, ZoneTreePersistenceFormat.IdentityFileName);
        var identityBytes = await File.ReadAllBytesAsync(identityPath);
        var sourceInventory = CaptureSourceInventory(fixture.Source);
        var position = store.Position;
        var oldSnapshotPath = fixture.Snapshot + ".checkpoint3";
        await File.WriteAllBytesAsync(oldSnapshotPath,
            EpochStorageFixture.CreateCurrentCheckpointAsLegacy(snapshotBytes));

        var oldInstall = Assert.ThrowsExactly<KeyLoadException>(() =>
            store.InstallSnapshot(oldSnapshotPath, snapshot.AppliedPosition));
        await Assert.That(oldInstall.Code).IsEqualTo(ErrorCode.FormatUnsupported);

        var afterSnapshot = store.CreateSnapshot(fixture.Snapshot + ".after-refusal", snapshot.AppliedPosition);
        var afterSnapshotBytes = await File.ReadAllBytesAsync(fixture.Snapshot + ".after-refusal");
        var afterIdentityBytes = await File.ReadAllBytesAsync(identityPath);
        await Assert.That(afterSnapshot).IsEqualTo(snapshot);
        await Assert.That(afterSnapshotBytes.SequenceEqual(snapshotBytes)).IsTrue();
        await Assert.That(afterIdentityBytes.SequenceEqual(identityBytes)).IsTrue();
        await Assert.That(store.Position).IsEqualTo(position);
        await Assert.That(CaptureSourceInventory(fixture.Source).SequenceEqual(sourceInventory)).IsTrue();
    }

    private static string[] CaptureSourceInventory(string directory)
        => Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(directory, path))
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static async Task AssertValuesAsync(ZoneTreeStore store, EpochStorageFixture fixture)
    {
        var values = store.Read(view => new[]
        {
            view.ReadOwnedValue(fixture.FirstKey),
            view.ReadOwnedValue(fixture.SecondKey)
        });
        await Assert.That(values[0]!).IsEquivalentTo(fixture.FirstValue, CollectionOrdering.Matching);
        await Assert.That(values[1]!).IsEquivalentTo(fixture.SecondValue, CollectionOrdering.Matching);
    }
}
