using System.Text;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.StorageRecovery;

internal static class JournalOriginalFailureCut
{
    private const string HealthyKeyText = "journal/healthy-after-reopen";
    private const string HealthyValueText = "real subsequent journal and canonical publication";
    private const long OriginalPosition = 1;
    private const long HealthyPosition = 2;
    private const int OriginalRows = 1;
    private const int HealthyRows = 2;
    private const int ExtraProbe = 1;
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
        var page = store.Read(view => view.Scan([], HealthyRows + ExtraProbe));
        await Assert.That(page.HasMore).IsFalse();
        await Assert.That(page.Records.Length).IsEqualTo(healthy ? HealthyRows : OriginalRows);
        await Assert.That(store.Position).IsEqualTo(healthy ? HealthyPosition : OriginalPosition);
        foreach (var (actual, literal) in page.Records.Zip(expected))
        {
            await Assert.That(actual.Key.Span.SequenceEqual(literal.Key)).IsTrue();
            await Assert.That(actual.Value.Span.SequenceEqual(literal.Value)).IsTrue();
        }
    }
}
