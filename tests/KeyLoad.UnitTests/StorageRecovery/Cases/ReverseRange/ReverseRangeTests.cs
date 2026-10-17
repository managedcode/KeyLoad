using System.Text;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class ReverseRangeTests
{
    private const int MaximumRecords = 100;
    private static readonly byte[] EmptyPrefix = [];
    private static readonly byte[] Prefix = [0x10, 0x20];
    private static readonly byte[] AllFfPrefix = [0xff, 0xff];

    [Test]
    public async Task AcRangeRev001CommittedKeysVisitInDescendingOrderWithinExclusiveBounds()
    {
        using var fixture = new ScopedRangeStoreFixture();
        Seed(fixture.Store);
        var keys = new List<string>();

        var result = fixture.Store.Read(view => view.VisitReverseRange(Key("items/"), MaximumRecords,
            (key, _) => { keys.Add(Encoding.UTF8.GetString(key)); return true; },
            afterKey: Key("items/1"), untilKey: Key("items/5")));

        await Assert.That(keys).IsEquivalentTo(new[] { "items/4", "items/3", "items/2" },
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(result.Records).IsEqualTo(3);
        await Assert.That(result.HasMore).IsFalse();
        await Assert.That(fixture.Store.Read(view => view.VisitReverseRange(Key("items/"), MaximumRecords,
            (_, _) => true, afterKey: Key("items/4"), untilKey: Key("items/2")).Records)).IsEqualTo(0);
    }

    [Test]
    public async Task AcRangeRev001PrefixSuccessorAndAllFfPrefixExcludeUnrelatedKeys()
    {
        using var fixture = new ScopedRangeStoreFixture();
        fixture.Store.Commit((tx, _) =>
        {
            tx.Put([0x10, 0x1f], [1]);
            tx.Put([0x10, 0x20], [2]);
            tx.Put([0x10, 0x20, 0x00], [3]);
            tx.Put([0x10, 0x20, 0xff], [4]);
            tx.Put([0x10, 0x21], [5]);
            tx.Put([0x10, 0x21, 0x00], [6]);
            tx.Put([0xff, 0xfe], [7]);
            tx.Put([0xff, 0xff], [8]);
            tx.Put([0xff, 0xff, 0x00], [9]);
            tx.Put([0xff, 0xff, 0xff], [10]);
            return true;
        });

        var bounded = new List<byte[]>();
        fixture.Store.Read(view => view.VisitReverseRange(Prefix, MaximumRecords,
            (key, _) => { bounded.Add(key.ToArray()); return true; }));
        var allFf = new List<byte[]>();
        fixture.Store.Read(view => view.VisitReverseRange(AllFfPrefix, MaximumRecords,
            (key, _) => { allFf.Add(key.ToArray()); return true; }));
        var empty = new List<byte[]>();
        fixture.Store.Read(view => view.VisitReverseRange(EmptyPrefix, MaximumRecords,
            (key, _) => { empty.Add(key.ToArray()); return true; }));

        await Assert.That(bounded.Select(Convert.ToHexString)).IsEquivalentTo(
            new[] { "1020FF", "102000", "1020" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(allFf.Select(Convert.ToHexString)).IsEquivalentTo(
            new[] { "FFFFFF", "FFFF00", "FFFF" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(empty.Select(Convert.ToHexString)).IsEquivalentTo(
            new[] { "FFFFFF", "FFFF00", "FFFF", "FFFE", "102100", "1021", "1020FF", "102000", "1020", "101F" },
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task AcRangeRev001ExplicitExclusiveUpperAndLowerBoundsAreAppliedToReverseSeek()
    {
        using var fixture = new ScopedRangeStoreFixture();
        fixture.Store.Commit((tx, _) =>
        {
            tx.Put([0x20], [1]);
            tx.Put([0x21], [2]);
            tx.Put([0x22], [3]);
            tx.Put([0x23], [4]);
            return true;
        });
        var keys = new List<string>();

        fixture.Store.Read(view => view.VisitReverseRange([], MaximumRecords,
            (key, _) => { keys.Add(Convert.ToHexString(key)); return true; },
            afterKey: [0x20], untilKey: [0x23]));

        await Assert.That(keys).IsEquivalentTo(new[] { "22", "21" },
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    private static void Seed(ZoneTreeStore store) => store.Commit((tx, _) =>
    {
        foreach (var name in new[] { "items/1", "items/2", "items/3", "items/4", "items/5", "outside/1" })
        {
            tx.Put(Key(name), Key(name));
        }
        return true;
    });

    private static byte[] Key(string text) => Encoding.UTF8.GetBytes(text);
}
