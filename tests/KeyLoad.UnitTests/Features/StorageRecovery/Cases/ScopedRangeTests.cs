using System.Text;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class ScopedRangeTests
{
    [Test]
    public async Task AcMp002RangeRespectsBoundsLimitAndVisitorStop()
    {
        using var fixture = new ScopedRangeStoreFixture();
        fixture.Store.Commit((tx, _) =>
        {
            foreach (var name in new[] { "a/1", "a/2", "a/3", "b/1" })
            {
                tx.Put(Key(name), Key(name));
            }
            return true;
        });
        var names = new List<string>();
        var observed = 0L;
        var range = fixture.Store.Read(view => view.VisitRange(Key("a/"), 2, (key, _) =>
        {
            names.Add(Encoding.UTF8.GetString(key));
            return true;
        }, observer: bytes => observed += bytes));
        await Assert.That(names).IsEquivalentTo(new[] { "a/1", "a/2" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(range.Records).IsEqualTo(2);
        await Assert.That(range.HasMore).IsTrue();
        await Assert.That(observed).IsEqualTo(6L * 3);
        names.Clear();
        observed = 0;
        var stopped = fixture.Store.Read(view => view.VisitRange(Key("a/"), 3, (key, _) =>
        {
            names.Add(Encoding.UTF8.GetString(key));
            return false;
        }, observer: bytes => observed += bytes));
        await Assert.That(names).HasSingleItem();
        await Assert.That(stopped.StoppedByVisitor).IsTrue();
        await Assert.That(stopped.HasMore).IsFalse();
        await Assert.That(observed).IsEqualTo(6L);
        names.Clear();
        var bounded = fixture.Store.Read(view => view.VisitRange(Key("a/"), 3, (key, _) =>
        {
            names.Add(Encoding.UTF8.GetString(key));
            return true;
        }, afterKey: Key("a/1"), untilKey: Key("a/3")));
        await Assert.That(names).IsEquivalentTo(new[] { "a/2" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(bounded.HasMore).IsFalse();
    }

    private static byte[] Key(string text) => Encoding.UTF8.GetBytes(text);

}
