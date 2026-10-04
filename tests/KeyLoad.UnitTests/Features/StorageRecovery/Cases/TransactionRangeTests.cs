using System.Globalization;
using System.Text;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class TransactionRangeTests
{
    private const int MaximumExaminedRecords = 100_000;
    private const string TombstonePrefix = "p/";
    private const string RangePrefix = "a/";

    [Test]
    public async Task AcMp002TransactionMergeChargesOverridesAndIgnoresOutOfRangeChanges()
    {
        using var fixture = new ScopedRangeStoreFixture();
        SeedRange(fixture.Store);
        var result = fixture.Store.Commit((transaction, _) => ApplyTransactionMerge(transaction));
        await Assert.That(result.Returned).IsEquivalentTo(new[] { "a/1:old", "a/2:new", "a/4:new" }, CollectionOrdering.Matching);
        await Assert.That(result.Bounded).IsEquivalentTo(new[] { "a/2" }, CollectionOrdering.Matching);
        await Assert.That(result.Observed).IsEqualTo(33L);
        await Assert.That(fixture.Store.Read(view => view.Scan(Key(RangePrefix), 10).Records.Length)).IsEqualTo(3);
    }

    [Test]
    public async Task AcMp002CallbackCancellationStopsStagedRangeBeforeTheNextRecordAndPreservesCommitCut()
    {
        using var fixture = new ScopedRangeStoreFixture();
        using var cancellation = new CancellationTokenSource();
        var position = fixture.Store.Position;
        var visits = 0;
        Task? cancellationCompletion = null;
        try
        {
            Assert.ThrowsExactly<OperationCanceledException>(() => fixture.Store.Commit((tx, _) =>
            {
                tx.Put(Key("staged/1"), Key("first"));
                tx.Put(Key("staged/2"), Key("second"));
                tx.VisitRange(Key("staged/"), 2, (_, _) =>
                {
                    visits++;
                    cancellationCompletion = cancellation.CancelAsync();
                    return true;
                }, cancellationToken: cancellation.Token);
                return true;
            }));
        }
        finally
        {
            if (cancellationCompletion is not null)
            {
                await cancellationCompletion;
            }
        }

        await Assert.That(visits).IsEqualTo(1);
        await Assert.That(fixture.Store.Position).IsEqualTo(position);
        await Assert.That(fixture.Store.Read(view => view.Scan(Key("staged/"), 2).Records)).IsEmpty();
        fixture.Store.Commit((tx, _) => { tx.Put(Key("healthy"), Key("yes")); return true; });
        await Assert.That(fixture.Store.Position).IsEqualTo(position + 1);
        await Assert.That(fixture.Store.Read(view => view.ReadOwnedValue(Key("healthy"))!)).IsEquivalentTo(Key("yes"));
    }

    [Test]
    public async Task AcMp002StagedTombstonesConsumeTheExaminedRecordCap()
    {
        using var fixture = new ScopedRangeStoreFixture();
        var observed = 0L;
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Store.Commit((tx, _) =>
        {
            for (var index = 0; index <= MaximumExaminedRecords; index++)
            {
                tx.Delete(Key(TombstonePrefix + index.ToString("D6", CultureInfo.InvariantCulture)));
            }
            tx.VisitRange(Key(TombstonePrefix), MaximumExaminedRecords, (_, _) => throw new InvalidOperationException("A tombstone was delivered."),
                observer: bytes => observed += bytes);
            return true;
        }));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(observed).IsGreaterThan(0L);
        fixture.Store.Commit((tx, _) => { tx.Put(Key("healthy"), Key("yes")); return true; });
        await Assert.That(fixture.Store.Read(view => view.ReadOwnedValue(Key("healthy"))!)).IsEquivalentTo(Key("yes"));
    }

    private static void SeedRange(ZoneTreeStore store) => store.Commit((tx, _) =>
    {
        foreach (var key in new[] { "a/1", "a/2", "a/3" })
        {
            tx.Put(Key(key), Key("old"));
        }
        return true;
    });

    private static (List<string> Returned, List<string> Bounded, long Observed) ApplyTransactionMerge(IAtomicTransaction transaction)
    {
        var observed = 0L;
        var returned = new List<string>();
        var bounded = new List<string>();
        transaction.Put(Key("z/large"), new byte[1_048_576]);
        transaction.Put(Key("a/2"), Key("new"));
        transaction.Delete(Key("a/3"));
        transaction.Put(Key("a/4"), Key("new"));
        var owned = transaction.ReadOwnedValue(Key("a/2"))!;
        owned[0] = 42;
        var page = transaction.VisitRange(Key(RangePrefix), 3, (key, value) =>
        {
            returned.Add(Encoding.UTF8.GetString(key) + ":" + Encoding.UTF8.GetString(value));
            return true;
        }, observer: bytes => observed += bytes);
        if (page.HasMore || page.Records != 3)
        {
            throw new InvalidOperationException("Unexpected merge page.");
        }
        transaction.VisitRange(Key(RangePrefix), 3, (key, _) =>
        {
            bounded.Add(Encoding.UTF8.GetString(key));
            return true;
        }, afterKey: Key("a/1"), untilKey: Key("a/4"));
        var pointBytes = 0L;
        var stagedFound = transaction.ReadValue(Key("a/2"), value =>
        {
            if (!value.SequenceEqual(Key("new")))
            {
                throw new InvalidOperationException("Staged value is absent.");
            }
        }, bytes => pointBytes = bytes);
        if (pointBytes != 6)
        {
            throw new InvalidOperationException("Staged point work was not charged.");
        }
        if (!stagedFound || transaction.ReadValue(Key("a/3"), _ => throw new InvalidOperationException("Tombstone leaked.")))
        {
            throw new InvalidOperationException("Staged lookup is inconsistent.");
        }
        return (returned, bounded, observed);
    }

    private static byte[] Key(string text) => Encoding.UTF8.GetBytes(text);

}
