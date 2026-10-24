using System.Text;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.StorageRecovery;

internal sealed class ReadCutConsistencyTests
{
    private const string FirstId = "one";
    private const string SecondId = "two";
    private const string ThirdId = "three";
    private const string OldFirst = "old-one";
    private const string OldSecond = "old-two";
    private const string NewFirst = "new-one";
    private const string NewThird = "new-three";

    [Test]
    public async Task AcCut001SnapshotKeepsExactOldCutAcrossCanonicalChanges()
    {
        using var fixture = new ReadCutFixture();
        fixture.Store.Commit((tx, _) =>
        {
            tx.Put(ReadCutFixture.Key(FirstId), ReadCutFixture.Value(OldFirst));
            tx.Put(ReadCutFixture.Key(SecondId), ReadCutFixture.Value(OldSecond));
            return true;
        });
        var expectedPosition = fixture.Store.Position;
        var expectedIdentity = fixture.Store.Identity;
        using var lease = fixture.Capture(ReadCutFixture.Limits(8, 256));
        AssertCut(lease.Cut, expectedIdentity, expectedPosition);

        fixture.Store.Commit((tx, _) =>
        {
            tx.Put(ReadCutFixture.Key(FirstId), ReadCutFixture.Value(NewFirst));
            tx.Delete(ReadCutFixture.Key(SecondId));
            tx.Put(ReadCutFixture.Key(ThirdId), ReadCutFixture.Value(NewThird));
            return true;
        });
        var oldRows = ReadSnapshot(lease);
        var currentRows = fixture.Store.Read(ReadCurrent);

        await Assert.That(oldRows).IsEquivalentTo(new[] { $"doc/{FirstId}={OldFirst}", $"doc/{SecondId}={OldSecond}" },
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(currentRows).IsEquivalentTo(new[] { $"doc/{FirstId}={NewFirst}", $"doc/{ThirdId}={NewThird}" },
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(fixture.Store.Position).IsEqualTo(expectedPosition + 1);
    }

    private static string[] ReadSnapshot(ZoneTreeReadCutLease lease)
    {
        var rows = new List<string>();
        var result = lease.VisitPrefix(ReadCutFixture.Key(string.Empty), (key, value) =>
        {
            rows.Add($"{Encoding.UTF8.GetString(key)}={Encoding.UTF8.GetString(value)}");
            return true;
        });
        if (result.Records != rows.Count || result.HasMore)
        {
            throw new InvalidOperationException("The execution read-cut result did not describe its delivered rows.");
        }
        return rows.ToArray();
    }

    private static string[] ReadCurrent(IKeyValueView view)
    {
        var rows = new List<string>();
        view.VisitRange(ReadCutFixture.Key(string.Empty), 8, (key, value) =>
        {
            rows.Add($"{Encoding.UTF8.GetString(key)}={Encoding.UTF8.GetString(value)}");
            return true;
        });
        return rows.ToArray();
    }

    private static void AssertCut(ZoneTreeReadCut cut, StoreIdentity identity, long position)
    {
        if (cut.Position != position || cut.FormatVersion != identity.FormatVersion
            || cut.KeyCodecVersion != identity.KeyCodecVersion || cut.NodeId != identity.NodeId
            || cut.Incarnation != identity.Incarnation || cut.Durability != identity.Durability
            || cut.DispatchPaused != identity.DispatchPaused || cut.ReadGeneration != identity.ReadGeneration)
        {
            throw new InvalidOperationException("The execution read-cut scalar identity does not match the gated cut.");
        }
    }
}
