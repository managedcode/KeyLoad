using System.Text;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class NativeReadCutReplacementTests
{
    private static byte[] AppliedKey => KeyCodec.Encode("system", "last-applied");
    private const string SnapshotValue = "snapshot";
    private const string OldValue = "old";
    private const string UpdatedValue = "updated";
    private const string AddedValue = "added";

    [Test]
    public async Task AcCut003InstallRejectsActiveLeaseWithoutTouchingTheOldTree()
    {
        var root = Path.Combine(Path.GetTempPath(), "keyload-read-cut-replace-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var incarnation = Guid.NewGuid();
        var installCallbacks = 0;
        try
        {
            var snapshotPath = CreateSnapshot(root, incarnation);

            using var target = new ZoneTreeStore(new(Path.Combine(root, "target"))
            {
                Incarnation = incarnation,
                FaultObserver = (_, _, _) => Interlocked.Increment(ref installCallbacks)
            });
            target.Commit((tx, _) =>
            {
                tx.Put(AppliedKey, NativeSerialization.Serialize(7L));
                tx.Put(Encoding.UTF8.GetBytes("doc/one"), Encoding.UTF8.GetBytes(OldValue));
                return true;
            });
            Interlocked.Exchange(ref installCallbacks, 0);
            var oldIdentity = target.Identity;
            var oldPosition = target.Position;
            using var lease = target.Read(view => target.CaptureNativeReadCut(view, NativeReadCutFixture.Limits(8, 256), CancellationToken.None));

            await AssertBlockedReplacement(target, snapshotPath, lease, oldIdentity, oldPosition, () => installCallbacks);
            await AssertCompactionAndWritesKeepCut(target, lease);

            lease.Dispose();
            Interlocked.Exchange(ref installCallbacks, 0);
            var installed = target.InstallSnapshot(snapshotPath, 7);
            await Assert.That(installCallbacks).IsGreaterThan(0);
            await Assert.That(installed.AppliedPosition).IsEqualTo(7);
            await Assert.That(target.Position).IsEqualTo(1L);
            await Assert.That(target.Identity.NodeId).IsEqualTo(oldIdentity.NodeId);
            await Assert.That(target.Identity.Incarnation).IsEqualTo(oldIdentity.Incarnation);
            await Assert.That(target.Identity.ReadGeneration).IsEqualTo(oldIdentity.ReadGeneration + 1);
            await Assert.That(target.Read(view => Encoding.UTF8.GetString(view.ReadOwnedValue(Encoding.UTF8.GetBytes("doc/one"))!)))
                .IsEqualTo(SnapshotValue);
            await Assert.That(target.Read(view => view.ReadOwnedValue(Encoding.UTF8.GetBytes("doc/two")))).IsNull();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateSnapshot(string root, Guid incarnation)
    {
        using var source = new ZoneTreeStore(new(Path.Combine(root, "source")) { Incarnation = incarnation });
        source.Commit((tx, _) =>
        {
            tx.Put(AppliedKey, NativeSerialization.Serialize(7L));
            tx.Put(Encoding.UTF8.GetBytes("doc/one"), Encoding.UTF8.GetBytes(SnapshotValue));
            return true;
        });
        var path = Path.Combine(root, "snapshot.bin");
        source.CreateSnapshot(path, 7);
        return path;
    }

    private static async Task AssertBlockedReplacement(ZoneTreeStore target, string snapshotPath,
        ZoneTreeReadCutLease lease, StoreIdentity identity, long position, Func<int> callbackCount)
    {
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => target.InstallSnapshot(snapshotPath, 7));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(callbackCount()).IsEqualTo(0);
        AssertStoreCut(target, identity, position);
        await Assert.That(target.Read(view => Encoding.UTF8.GetString(view.ReadOwnedValue(Encoding.UTF8.GetBytes("doc/one"))!)))
            .IsEqualTo(OldValue);
        await Assert.That(ReadRows(lease, expectedPriorRecords: 0)).IsEquivalentTo(new[] { "doc/one=" + OldValue },
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    private static async Task AssertCompactionAndWritesKeepCut(ZoneTreeStore target, ZoneTreeReadCutLease lease)
    {
        target.Compact();
        target.Commit((tx, _) =>
        {
            tx.Put(Encoding.UTF8.GetBytes("doc/one"), Encoding.UTF8.GetBytes(UpdatedValue));
            tx.Put(Encoding.UTF8.GetBytes("doc/two"), Encoding.UTF8.GetBytes(AddedValue));
            return true;
        });
        await Assert.That(ReadRows(lease, expectedPriorRecords: 1)).IsEquivalentTo(new[] { "doc/one=" + OldValue },
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    private static string[] ReadRows(ZoneTreeReadCutLease lease, int expectedPriorRecords)
    {
        var rows = new List<string>();
        var result = lease.VisitPrefix(Encoding.UTF8.GetBytes("doc/"), (key, value) =>
        {
            rows.Add(Encoding.UTF8.GetString(key) + "=" + Encoding.UTF8.GetString(value));
            return true;
        });
        if (result.Records != expectedPriorRecords + rows.Count || result.HasMore)
        {
            throw new InvalidOperationException("The native snapshot did not report its complete rows.");
        }
        return rows.ToArray();
    }

    private static void AssertStoreCut(ZoneTreeStore store, StoreIdentity expectedIdentity, long expectedPosition)
    {
        var actual = store.Identity;
        if (store.Position != expectedPosition || actual.FormatVersion != expectedIdentity.FormatVersion
            || actual.KeyCodecVersion != expectedIdentity.KeyCodecVersion || actual.NodeId != expectedIdentity.NodeId
            || actual.Incarnation != expectedIdentity.Incarnation
            || actual.ReadGeneration != expectedIdentity.ReadGeneration || actual.Durability != expectedIdentity.Durability
            || actual.DispatchPaused != expectedIdentity.DispatchPaused
            || !actual.SigningKey.Span.SequenceEqual(expectedIdentity.SigningKey.Span))
        {
            throw new InvalidOperationException("A rejected native tree replacement changed the live store cut.");
        }
    }
}
