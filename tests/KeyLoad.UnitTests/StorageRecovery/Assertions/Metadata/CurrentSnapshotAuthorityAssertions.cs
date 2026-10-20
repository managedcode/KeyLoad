using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class CurrentSnapshotAuthorityAssertions
{
    private const long OriginalPosition = 1;
    private const long OriginalAppliedPosition = 1;
    private const long SnapshotRecords = 3;
    private const long NextGeneration = 1;
    private const long RestoreIncrement = 1;
    private const long HealthyIncrement = 1;
    private const int MaximumRows = 5;
    private static readonly byte[] FollowupKey = [0x02, 0x44, 0x10];
    private static readonly byte[] FollowupValue = [0x83, 0x00, 0x45];

    internal static async Task RequireSourceAsync(ZoneTreeStore store, CurrentStorageFixture fixture,
        StoreIdentity original, StorageSnapshot snapshot)
    {
        await RequireSnapshotAsync(snapshot, original);
        await RequireIdentityAsync(original with
        {
            ReadGeneration = original.ReadGeneration + NextGeneration
        }, store.Identity);
        await Assert.That(store.Position).IsEqualTo(OriginalPosition);
        await RequireRowsAsync(store, fixture, restored: false, healthy: false);
    }

    internal static async Task RequireSnapshotAsync(StorageSnapshot snapshot, StoreIdentity original)
    {
        await Assert.That(snapshot.Incarnation).IsEqualTo(original.Incarnation);
        await Assert.That(snapshot.Position).IsEqualTo(OriginalPosition);
        await Assert.That(snapshot.AppliedPosition).IsEqualTo(OriginalAppliedPosition);
        await Assert.That(snapshot.RecordCount).IsEqualTo(SnapshotRecords);
    }

    internal static async Task RequireRestoreIdentityAsync(StoreIdentity source, StoreIdentity restored,
        Guid incarnation)
    {
        await Assert.That(restored.NodeId).IsNotEqualTo(source.NodeId);
        await Assert.That(restored.NodeId).IsNotEqualTo(Guid.Empty);
        await Assert.That(restored.Incarnation).IsEqualTo(incarnation);
        await Assert.That(restored.Incarnation).IsNotEqualTo(source.Incarnation);
        await Assert.That(restored.SigningKey.Span.SequenceEqual(source.SigningKey.Span)).IsFalse();
        await Assert.That(restored.SigningKey.Length).IsEqualTo(source.SigningKey.Length);
        await Assert.That(restored.FormatVersion).IsEqualTo(CurrentStorageFixture.CurrentFormatVersion);
        await Assert.That(restored.KeyCodecVersion).IsEqualTo(source.KeyCodecVersion);
        await Assert.That(restored.Durability).IsEqualTo(source.Durability);
        await Assert.That(restored.DispatchPaused).IsTrue();
        await Assert.That(restored.ReadGeneration).IsEqualTo(source.ReadGeneration + NextGeneration);
        await Assert.That(restored.MinimumReaderContract).IsEqualTo(StoreReaderContract.RuntimeJournal);
        await Assert.That(restored.MinimumReaderContract).IsEqualTo(source.MinimumReaderContract);
    }

    internal static async Task CompleteColdAsync(CurrentStorageFixture fixture, StoreIdentity original,
        StoreIdentity restoredIdentity, StorageSnapshot snapshot, Dictionary<string, byte[]?> backup, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await CurrentStorageSnapshot.AssertUnchangedAsync(fixture.Backup, backup);
        using (var source = Open(fixture.Source))
        {
            await RequireSourceAsync(source, fixture, original, snapshot);
        }
        using (var restored = Open(fixture.Restored))
        {
            await RequireRestoredAsync(restored, fixture, restoredIdentity, healthy: false);
            token.ThrowIfCancellationRequested();
            restored.Commit((transaction, _) => { transaction.Put(FollowupKey, FollowupValue); return true; });
            await RequireRestoredAsync(restored, fixture, restoredIdentity, healthy: true);
        }
        await RequireRestoredColdAsync(fixture, restoredIdentity, token);
        await RequireRestoredColdAsync(fixture, restoredIdentity, token);
        await CurrentStorageSnapshot.AssertUnchangedAsync(fixture.Backup, backup);
        using var finalSource = Open(fixture.Source);
        await RequireSourceAsync(finalSource, fixture, original, snapshot);
    }

    private static ZoneTreeStore Open(string directory) => new(new(directory),
        UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());

    private static async Task RequireRestoredColdAsync(CurrentStorageFixture fixture, StoreIdentity expected, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var restored = Open(fixture.Restored);
        await RequireRestoredAsync(restored, fixture, expected, healthy: true);
    }

    private static async Task RequireRestoredAsync(ZoneTreeStore store, CurrentStorageFixture fixture,
        StoreIdentity expected, bool healthy)
    {
        await RequireIdentityAsync(expected, store.Identity);
        await Assert.That(store.Position).IsEqualTo(OriginalPosition + RestoreIncrement + (healthy ? HealthyIncrement : 0));
        await RequireRowsAsync(store, fixture, restored: true, healthy);
    }

    private static async Task RequireIdentityAsync(StoreIdentity expected, StoreIdentity actual)
    {
        await WalFileFixture.AssertPreservedIdentity(expected, actual);
        await Assert.That(actual.MinimumReaderContract).IsEqualTo(StoreReaderContract.RuntimeJournal);
        await Assert.That(actual.MinimumReaderContract).IsEqualTo(expected.MinimumReaderContract);
    }

    private static async Task RequireRowsAsync(ZoneTreeStore store, CurrentStorageFixture fixture,
        bool restored, bool healthy)
    {
        var expected = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [Convert.ToHexString(fixture.FirstKey)] = fixture.FirstValue,
            [Convert.ToHexString(fixture.SecondKey)] = fixture.SecondValue
        };
        expected.Add(Convert.ToHexString(KeyCodec.Encode(ZoneTreePersistenceFormat.SystemNamespace,
            ZoneTreePersistenceFormat.LastAppliedKey)), OrleansSerialization.Serialize(OriginalAppliedPosition));
        if (restored)
        {
            expected.Add(Convert.ToHexString(KeyCodec.Encode(ZoneTreePersistenceFormat.SystemNamespace,
                ZoneTreePersistenceFormat.DispatchPausedKey)), OrleansSerialization.Serialize(true));
        }
        if (healthy)
        {
            expected.Add(Convert.ToHexString(FollowupKey), FollowupValue);
        }
        var page = store.Read(view => view.Scan([], MaximumRows + 1));
        await Assert.That(page.HasMore).IsFalse();
        await Assert.That(page.Records.Length).IsEqualTo(expected.Count);
        var actual = page.Records.ToDictionary(row => Convert.ToHexString(row.Key.Span), row => row.Value.ToArray());
        await Assert.That(actual.Keys.Order(StringComparer.Ordinal))
            .IsEquivalentTo(expected.Keys.Order(StringComparer.Ordinal), CollectionOrdering.Matching);
        foreach (var row in expected)
        {
            await Assert.That(actual[row.Key]).IsEquivalentTo(row.Value, CollectionOrdering.Matching);
        }
    }
}
