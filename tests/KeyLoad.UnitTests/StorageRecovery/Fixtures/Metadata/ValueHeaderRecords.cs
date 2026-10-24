using System.Text;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.StorageRecovery;

internal static class ValueHeaderRecords
{
    internal const long SeedPosition = 1;
    internal const long HealthyPosition = 2;
    internal const int CompleteRecords = 2;
    internal const int MarkerBytes = 1;
    private const byte InvalidMarker = 2;
    private const string HealthyKeyText = "healthy/execution-header";
    private const string HealthyValueText = "canonical continuation after execution header repair";
    internal const string SnapshotFile = "execution-header-rejected.snapshot";

    internal static byte[] HealthyKey => Encoding.UTF8.GetBytes(HealthyKeyText);
    internal static byte[] HealthyValue => Encoding.UTF8.GetBytes(HealthyValueText);

    internal static byte[] InvalidBytes(ZoneTreeExistingStoreFixture files)
    {
        var bytes = new byte[files.Value.Length + MarkerBytes];
        bytes[0] = InvalidMarker;
        files.Value.CopyTo(bytes.AsSpan(MarkerBytes));
        return bytes;
    }

    internal static void Corrupt(ZoneTreeStoreRuntime runtime, ZoneTreeExistingStoreFixture files)
    {
        runtime.Gate.EnterWriteLock();
        try
        {
            runtime.Tree.Upsert(files.Key, InvalidBytes(files));
        }
        finally
        {
            runtime.Gate.ExitWriteLock();
        }
    }

    internal static async Task RowsAsync(ZoneTreeStore store, ZoneTreeExistingStoreFixture files, bool healthy)
    {
        await Assert.That(OrleansSerialization.Serialize(store.Identity).AsSpan()
            .SequenceEqual(OrleansSerialization.Serialize(files.Identity))).IsTrue();
        await Assert.That(store.Position).IsEqualTo(healthy ? HealthyPosition : SeedPosition);
        var owned = store.Read(view => view.ReadOwnedValue(files.Key));
        await Assert.That(owned).IsNotNull();
        await Assert.That(owned!.AsSpan().SequenceEqual(files.Value)).IsTrue();
        var borrowedMatches = false;
        var found = store.Read(view => view.ReadValue(files.Key,
            value => borrowedMatches = value.SequenceEqual(files.Value)));
        await Assert.That(found).IsTrue();
        await Assert.That(borrowedMatches).IsTrue();
        var expected = new List<KeyValueRecord> { new(files.Key, files.Value) };
        if (healthy)
        {
            expected.Add(new(HealthyKey, HealthyValue));
        }
        var actual = store.Read(view => view.Scan([], CompleteRecords));
        await Assert.That(actual.HasMore).IsFalse();
        await Assert.That(actual.Records.Length).IsEqualTo(expected.Count);
        for (var index = 0; index < expected.Count; index++)
        {
            await Assert.That(actual.Records[index].Key.Span.SequenceEqual(expected[index].Key.Span)).IsTrue();
            await Assert.That(actual.Records[index].Value.Span.SequenceEqual(expected[index].Value.Span)).IsTrue();
        }
    }
}
