using System.Text;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class ReverseTransactionRangeTests
{
    private const int MaximumRecords = 100;
    private const string Prefix = "records/";

    [Test]
    public async Task AcRangeRev002DescendingOverlayAppliesReplacementDeleteAndInsertExactlyOnce()
    {
        using var fixture = new ScopedRangeStoreFixture();
        fixture.Store.Commit((tx, _) =>
        {
            tx.Put(Key("records/1"), Key("old-1"));
            tx.Put(Key("records/2"), Key("old-2"));
            tx.Put(Key("records/3"), Key("old-3"));
            tx.Put(Key("records/5"), Key("old-5"));
            return true;
        });

        var visits = new List<string>();
        fixture.Store.Commit((tx, _) =>
        {
            tx.Put(Key("records/2"), Key("replacement-2"));
            tx.Delete(Key("records/3"));
            tx.Put(Key("records/4"), Key("insert-4"));
            var result = tx.VisitReverseRange(Key(Prefix), MaximumRecords, (key, value) =>
            {
                visits.Add(Encoding.UTF8.GetString(key) + ":" + Encoding.UTF8.GetString(value));
                return true;
            });
            if (result.Records != 4 || result.HasMore)
            {
                throw new InvalidOperationException("The reverse overlay returned an incomplete range.");
            }
            return true;
        });

        await Assert.That(visits).IsEquivalentTo(new[]
        {
            "records/5:old-5", "records/4:insert-4", "records/2:replacement-2", "records/1:old-1"
        }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(fixture.Store.Read(view => view.Scan(Key(Prefix), MaximumRecords).Records.Length)).IsEqualTo(4);
    }

    [Test]
    public async Task AcRangeRev002TransactionBoundsStayExclusiveInDescendingOverlay()
    {
        using var fixture = new ScopedRangeStoreFixture();
        fixture.Store.Commit((tx, _) =>
        {
            foreach (var suffix in new[] { "1", "2", "3", "4", "5" })
            {
                tx.Put(Key(Prefix + suffix), Key(suffix));
            }
            return true;
        });
        var visits = new List<string>();

        fixture.Store.Commit((tx, _) =>
        {
            tx.Put(Key(Prefix + "3"), Key("staged-3"));
            tx.Put(Key(Prefix + "4"), Key("staged-4"));
            tx.VisitReverseRange(Key(Prefix), MaximumRecords, (key, _) =>
            {
                visits.Add(Encoding.UTF8.GetString(key));
                return true;
            }, afterKey: Key(Prefix + "1"), untilKey: Key(Prefix + "5"));
            return true;
        });

        await Assert.That(visits).IsEquivalentTo(new[] { "records/4", "records/3", "records/2" },
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    private static byte[] Key(string text) => Encoding.UTF8.GetBytes(text);
}
