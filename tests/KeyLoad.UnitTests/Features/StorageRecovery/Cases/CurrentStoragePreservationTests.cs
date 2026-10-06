using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class CurrentStoragePreservationTests
{
    private const int SourcePosition = 1;
    private const int ChecksumOffsetFromEnd = 1;
    private const byte ChangedByte = 1;

    [Test]
    public async Task CurrentMaintenanceCheckpointInstallBackupAndRestoreRetainCurrentFormat()
    {
        using var fixture = new CurrentStorageFixture();
        StorageSnapshot snapshot;
        using (var store = new ZoneTreeStore(fixture.SourceOptions, UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution()))
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
            await AssertCurrentFormatAsync(store.Identity);
        }

        await AssertCorruptSnapshotPreservesCurrentStoreAsync(fixture, snapshot);
        using (var store = new ZoneTreeStore(fixture.SourceOptions, UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution()))
        {
            store.Compact();
            await AssertCurrentFormatAsync(store.Identity);
            var installed = store.InstallSnapshot(fixture.Snapshot, snapshot.AppliedPosition);
            await Assert.That(installed.Position).IsEqualTo(SourcePosition);
            await AssertCurrentFormatAsync(store.Identity);
            await AssertValuesAsync(store, fixture);
            store.CreateBackup(fixture.Backup);
        }

        var restoredIdentity = ZoneTreeStore.Restore(fixture.Backup, fixture.Restored, UnitExecutionOptions.StorageExecution());
        await AssertCurrentFormatAsync(restoredIdentity);
        using var restored = new ZoneTreeStore(new(fixture.Restored), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        await AssertCurrentFormatAsync(restored.Identity);
        await AssertValuesAsync(restored, fixture);
    }

    private static async Task AssertCurrentFormatAsync(StoreIdentity identity)
        => await Assert.That(identity.FormatVersion).IsEqualTo(CurrentStorageFixture.CurrentFormatVersion);

    private static async Task AssertCorruptSnapshotPreservesCurrentStoreAsync(CurrentStorageFixture fixture,
        StorageSnapshot snapshot)
    {
        var snapshotBytes = await File.ReadAllBytesAsync(fixture.Snapshot);
        var invalidBytes = snapshotBytes.ToArray();
        invalidBytes[^ChecksumOffsetFromEnd] ^= ChangedByte;
        await File.WriteAllBytesAsync(fixture.InvalidSnapshot, invalidBytes);
        using (var store = new ZoneTreeStore(fixture.SourceOptions, UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution()))
        {
            var sourceInventory = await CurrentStorageSnapshot.CaptureAsync(fixture.Source);
            var position = store.Position;
            var identity = store.Identity;
            var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
                store.InstallSnapshot(fixture.InvalidSnapshot, snapshot.AppliedPosition));

            await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
            await Assert.That(store.Position).IsEqualTo(position);
            await Assert.That(store.Identity.FormatVersion).IsEqualTo(identity.FormatVersion);
            await Assert.That(store.Identity.NodeId).IsEqualTo(identity.NodeId);
            await Assert.That(store.Identity.Incarnation).IsEqualTo(identity.Incarnation);
            await Assert.That(store.Identity.KeyCodecVersion).IsEqualTo(identity.KeyCodecVersion);
            await Assert.That(store.Identity.SigningKey.Span.SequenceEqual(identity.SigningKey.Span)).IsTrue();
            await Assert.That(store.Identity.Durability).IsEqualTo(identity.Durability);
            await Assert.That(store.Identity.DispatchPaused).IsEqualTo(identity.DispatchPaused);
            await Assert.That(store.Identity.ReadGeneration).IsEqualTo(identity.ReadGeneration);
            await CurrentStorageSnapshot.AssertUnchangedAsync(fixture.Source, sourceInventory);
        }
        await Assert.That((await File.ReadAllBytesAsync(fixture.Snapshot)).AsSpan().SequenceEqual(snapshotBytes)).IsTrue();
    }

    private static async Task AssertValuesAsync(ZoneTreeStore store, CurrentStorageFixture fixture)
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
