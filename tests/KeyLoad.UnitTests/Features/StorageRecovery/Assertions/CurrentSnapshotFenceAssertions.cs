using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class CurrentSnapshotFenceAssertions
{
    internal static async Task RejectAsync(ZoneTreeStore store, string root, string snapshot,
        long applied, ErrorCode code, bool installed)
    {
        var files = await CurrentStorageSnapshot.CaptureAsync(root);
        var snapshotBytes = await File.ReadAllBytesAsync(snapshot);
        var identity = store.Identity;
        var position = store.Position;
        var error = Assert.ThrowsExactly<KeyLoadException>(() => store.InstallSnapshot(snapshot, applied));
        await Assert.That(error.Code).IsEqualTo(code);
        await RequireIdentityAsync(identity, store.Identity);
        await Assert.That(store.Position).IsEqualTo(position);
        await CurrentStorageSnapshot.AssertUnchangedAsync(root, files);
        await Assert.That(await File.ReadAllBytesAsync(snapshot))
            .IsEquivalentTo(snapshotBytes, CollectionOrdering.Matching);
        await RequireRowsAsync(store, installed, healthy: false);
    }

    internal static async Task RequireIdentityAsync(StoreIdentity expected, StoreIdentity actual)
    {
        await WalFileFixture.AssertPreservedIdentity(expected, actual);
        await Assert.That(actual.MinimumReaderContract).IsEqualTo(expected.MinimumReaderContract);
        await Assert.That(actual.MinimumReaderContract).IsEqualTo(StoreReaderContract.RuntimeJournal);
    }

    internal static async Task RequireRowsAsync(ZoneTreeStore store, bool installed, bool healthy)
    {
        var expected = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        if (installed)
        {
            expected.Add(Convert.ToHexString(KeyCodec.Encode("new")), NativeSerialization.Serialize("value"));
            expected.Add(Convert.ToHexString(KeyCodec.Encode("system", "last-applied")), NativeSerialization.Serialize(8L));
        }
        if (healthy)
        {
            expected.Add(Convert.ToHexString(KeyCodec.Encode("followup")), NativeSerialization.Serialize("current-fence"));
        }
        var rows = store.Read(view => view.Scan([], 4));
        await Assert.That(rows.HasMore).IsFalse();
        await Assert.That(rows.Records.Length).IsEqualTo(expected.Count);
        var actual = rows.Records.ToDictionary(row => Convert.ToHexString(row.Key.Span), row => row.Value.ToArray());
        await Assert.That(actual.Keys.Order(StringComparer.Ordinal))
            .IsEquivalentTo(expected.Keys.Order(StringComparer.Ordinal), CollectionOrdering.Matching);
        foreach (var row in expected)
        {
            await Assert.That(actual[row.Key]).IsEquivalentTo(row.Value, CollectionOrdering.Matching);
        }
        await Assert.That(store.Read(view => view.ReadOwnedValue(KeyCodec.Encode("obsolete")))).IsNull();
    }
}
