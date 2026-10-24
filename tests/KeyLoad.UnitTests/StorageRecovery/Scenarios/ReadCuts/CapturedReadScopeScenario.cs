using System.Text;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.StorageRecovery.Assertions;

namespace KeyLoad.UnitTests.StorageRecovery;

internal static class CapturedReadScopeScenario
{
    internal const string Point = "point";
    internal const string Missing = "missing";
    internal const string Group = "group/";
    internal const string First = "group/a";
    internal const string Second = "group/b";
    internal const string Added = "group/c";
    internal const string Unknown = "unadmitted";
    internal const string Healthy = "healthy";
    internal const string OldPoint = "old-point";
    internal const string NewPoint = "new-point";
    internal const string OldFirst = "old-first";
    internal const string OldSecond = "old-second";
    internal const string NewFirst = "new-first";
    internal const string NewAdded = "new-added";
    internal const string HealthyValue = "healthy-value";
    internal const int MaximumRecords = 16;
    internal const long MaximumBytes = 65_536;
    internal const int RuntimeHeaderBytes = 1;
    private const string RecordSeparator = "=";
    private const string AdmissionExceeded = "The captured test operation exceeded its retained admission.";

    internal static void Seed(ZoneTreeStore store)
        => store.Commit((tx, _) =>
        {
            Put(tx, Point, OldPoint);
            Put(tx, First, OldFirst);
            Put(tx, Second, OldSecond);
            return true;
        });

    internal static void Change(ZoneTreeStore store)
        => store.Commit((tx, _) =>
        {
            Put(tx, Point, NewPoint);
            Put(tx, First, NewFirst);
            tx.Delete(ReadCutFixture.Key(Second));
            Put(tx, Added, NewAdded);
            Put(tx, Missing, HealthyValue);
            return true;
        });

    internal static ZoneTreeCapturedReadView Capture(ZoneTreeReadCutLease lease)
    {
        long bytes = 0;
        var records = 0;
        var builder = new ZoneTreeCapturedReadBuilder(lease, (nextBytes, nextRecords) =>
        {
            bytes = checked(bytes + nextBytes);
            records = checked(records + nextRecords);
            if (bytes > MaximumBytes || records > MaximumRecords)
            {
                throw Errors.Fail(ErrorCode.BudgetExceeded, AdmissionExceeded);
            }
        });
        try
        {
            builder.CaptureExact(ReadCutFixture.Key(Point));
            builder.CaptureExact(ReadCutFixture.Key(Missing));
            builder.CapturePrefix(ReadCutFixture.Key(Group));
            return builder.Transfer();
        }
        finally
        {
            builder.ClearUntransferred();
        }
    }

    internal static void CommitHealthy(ZoneTreeStore store)
        => store.Commit((tx, _) =>
        {
            Put(tx, Healthy, HealthyValue);
            return true;
        });

    internal static async Task RequireCutAsync(ZoneTreeReadCut cut, StoreIdentity identity, long position)
    {
        await Assert.That(cut.Position).IsEqualTo(position);
        await Assert.That(cut.FormatVersion).IsEqualTo(identity.FormatVersion);
        await Assert.That(cut.KeyCodecVersion).IsEqualTo(identity.KeyCodecVersion);
        await Assert.That(cut.NodeId).IsEqualTo(identity.NodeId);
        await Assert.That(cut.Incarnation).IsEqualTo(identity.Incarnation);
        await Assert.That(cut.Durability).IsEqualTo(identity.Durability);
        await Assert.That(cut.DispatchPaused).IsEqualTo(identity.DispatchPaused);
        await Assert.That(cut.ReadGeneration).IsEqualTo(identity.ReadGeneration);
    }

    internal static async Task RequireCurrentAsync(ZoneTreeStore store, StoreIdentity identity,
        long position, bool changed, bool healthy)
    {
        await ReadCutStoreStateAssertions.AssertIdentityAndPositionAsync(store, identity, position);
        var expected = changed
            ? new[] { Row(First, NewFirst), Row(Added, NewAdded), Row(Missing, HealthyValue), Row(Point, NewPoint) }
            : new[] { Row(First, OldFirst), Row(Second, OldSecond), Row(Point, OldPoint) };
        if (healthy)
        {
            expected = [.. expected, Row(Healthy, HealthyValue)];
        }
        Array.Sort(expected, StringComparer.Ordinal);
        var actual = store.Read(ReadRows);
        await Assert.That(actual).IsEquivalentTo(expected, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    internal static async Task RequireFrozenAsync(ZoneTreeCapturedReadView frozen)
    {
        await Assert.That(frozen.ReadOwnedValue(ReadCutFixture.Key(Point))!.SequenceEqual(
            ReadCutFixture.Value(OldPoint))).IsTrue();
        await Assert.That(frozen.ReadOwnedValue(ReadCutFixture.Key(Missing))).IsNull();
        var rows = new List<string>();
        var forward = frozen.VisitRange(ReadCutFixture.Key(Group), MaximumRecords, (key, value) =>
        {
            rows.Add(Encoding.UTF8.GetString(key) + RecordSeparator + Encoding.UTF8.GetString(value));
            return true;
        });
        await Assert.That(rows).IsEquivalentTo(new[] { Row(First, OldFirst), Row(Second, OldSecond) },
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(forward.Records).IsEqualTo(rows.Count);
        await Assert.That(forward.HasMore).IsFalse();
        rows.Clear();
        var reverse = frozen.VisitReverseRange(ReadCutFixture.Key(Group), MaximumRecords, (key, value) =>
        {
            rows.Add(Encoding.UTF8.GetString(key) + RecordSeparator + Encoding.UTF8.GetString(value));
            return true;
        });
        await Assert.That(rows).IsEquivalentTo(new[] { Row(Second, OldSecond), Row(First, OldFirst) },
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(reverse.Records).IsEqualTo(rows.Count);
        await Assert.That(reverse.HasMore).IsFalse();
    }

    private static string[] ReadRows(IKeyValueView view)
    {
        var rows = new List<string>();
        var result = view.VisitRange([], MaximumRecords, (key, value) =>
        {
            rows.Add(Encoding.UTF8.GetString(key) + RecordSeparator + Encoding.UTF8.GetString(value));
            return true;
        });
        if (result.HasMore)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, AdmissionExceeded);
        }
        return [.. rows];
    }

    private static string Row(string key, string value)
        => Encoding.UTF8.GetString(ReadCutFixture.Key(key)) + RecordSeparator + value;

    private static void Put(IAtomicTransaction tx, string key, string value)
        => tx.Put(ReadCutFixture.Key(key), ReadCutFixture.Value(value));
}
