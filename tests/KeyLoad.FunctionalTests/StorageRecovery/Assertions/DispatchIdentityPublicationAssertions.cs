using System.Text;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.FunctionalTests.StorageRecovery;

internal static class DispatchIdentityPublicationAssertions
{
    private const long SeedPosition = 1;
    private const long HealthyPosition = 2;
    private const int OriginalRows = 1;
    private const int HealthyRows = 2;
    private const int ExtraRowProbe = 1;
    private const string HealthyKeyText = "identity/publication/healthy";
    private const string HealthyValueText = "canonical continuation after identity publication repair";
    private static readonly byte[] HealthyKey = Encoding.UTF8.GetBytes(HealthyKeyText);
    private static readonly byte[] HealthyValue = Encoding.UTF8.GetBytes(HealthyValueText);

    internal static void CommitHealthy(ZoneTreeStore store)
    {
        store.Commit((transaction, _) =>
        {
            transaction.Put(HealthyKey, HealthyValue);
            return true;
        });
    }

    internal static async Task IdentityAsync(ZoneTreeStore store, string path, StoreIdentity expected)
    {
        var expectedBytes = OrleansSerialization.Serialize(expected);
        await Assert.That(OrleansSerialization.Serialize(store.Identity).AsSpan().SequenceEqual(expectedBytes)).IsTrue();
        var originalFile = await File.ReadAllBytesAsync(path);
        var persisted = ZoneTreeIdentityFile.Read(originalFile);
        await Assert.That(OrleansSerialization.Serialize(persisted).AsSpan().SequenceEqual(expectedBytes)).IsTrue();
    }

    internal static async Task RowsAsync(ZoneTreeStore store, ZoneTreeExistingStoreFixture files, bool healthy)
    {
        var expected = new List<(byte[] Key, byte[] Value)>
        {
            (files.Key, files.Value)
        };
        if (healthy)
        {
            expected.Add((HealthyKey, HealthyValue));
        }
        var page = store.Read(view => view.Scan([], HealthyRows + ExtraRowProbe));
        await Assert.That(page.HasMore).IsFalse();
        await Assert.That(page.Records.Length).IsEqualTo(healthy ? HealthyRows : OriginalRows);
        await Assert.That(store.Position).IsEqualTo(healthy ? HealthyPosition : SeedPosition);
        foreach (var (actual, literal) in page.Records.Zip(expected))
        {
            await Assert.That(actual.Key.Span.SequenceEqual(literal.Key)).IsTrue();
            await Assert.That(actual.Value.Span.SequenceEqual(literal.Value)).IsTrue();
        }
    }
}
