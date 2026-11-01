using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.StorageRecovery;

internal sealed class TombstoneValueTests
{
    [Test]
    public async Task AcPsw002EmptyPutAndDeleteRemainDistinctThroughStageCommitAndReopen()
    {
        var directory = CreateDirectory();
        var prefix = new byte[] { 0xA0 };
        var emptyKey = new byte[] { 0xA0, 0x01 };
        var deletedKey = new byte[] { 0xA0, 0x02 };
        var retainedKey = new byte[] { 0xA0, 0x03 };
        long position;
        try
        {
            using (var store = new ZoneTreeStore(new(directory), UnitExecutionOptions.StorageExecution()))
            {
                store.Commit((tx, _) =>
                {
                    tx.Put(emptyKey, [0xFF]);
                    tx.Put(deletedKey, [0xFE]);
                    tx.Put(retainedKey, [0xFD]);
                    return true;
                });
                await AssertStagedView(store, prefix, emptyKey, deletedKey, retainedKey);
                await AssertCommittedView(store, prefix, emptyKey, deletedKey, retainedKey);
            }

            using (var replayed = new ZoneTreeStore(new(directory), UnitExecutionOptions.StorageExecution()))
            {
                await AssertCommittedView(replayed, prefix, emptyKey, deletedKey, retainedKey);
                position = replayed.Position;
            }

            using var reopened = new ZoneTreeStore(new(directory), UnitExecutionOptions.StorageExecution());
            await AssertCommittedView(reopened, prefix, emptyKey, deletedKey, retainedKey);
            await Assert.That(reopened.Position).IsEqualTo(position);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task AssertCommittedView(ZoneTreeStore store, byte[] prefix,
        byte[] emptyKey, byte[] deletedKey, byte[] retainedKey)
    {
        var ownedEmpty = store.Read(view => view.ReadOwnedValue(emptyKey));
        var deletedOwned = store.Read(view => view.ReadOwnedValue(deletedKey));
        var borrowedLength = -1;
        var borrowedEmpty = store.Read(view => view.ReadValue(emptyKey, value => borrowedLength = value.Length));
        var deletedCallback = false;
        var borrowedDelete = store.Read(view => view.ReadValue(deletedKey, _ => deletedCallback = true));
        var range = store.Read(view => view.Scan(prefix, 10));
        var orderedKeys = range.Records.Select(record => Convert.ToHexString(record.Key.Span)).ToArray();

        await Assert.That(ownedEmpty).IsNotNull();
        await Assert.That(ownedEmpty).IsEmpty();
        await Assert.That(deletedOwned).IsNull();
        await Assert.That(borrowedEmpty).IsTrue();
        await Assert.That(borrowedLength).IsEqualTo(0);
        await Assert.That(borrowedDelete).IsFalse();
        await Assert.That(deletedCallback).IsFalse();
        await Assert.That(range.Records.Length).IsEqualTo(2);
        await Assert.That(orderedKeys).IsEquivalentTo(["A001", "A003"], CollectionOrdering.Matching);
        await Assert.That(range.Records[0].Key.Span.SequenceEqual(emptyKey)).IsTrue();
        await Assert.That(range.Records[0].Value.Length).IsEqualTo(0);
        await Assert.That(range.Records[1].Key.Span.SequenceEqual(retainedKey)).IsTrue();
        await Assert.That(range.Records[1].Value.Span.SequenceEqual(new byte[] { 0xFD })).IsTrue();
    }

    private static async Task AssertStagedView(ZoneTreeStore store, byte[] prefix,
        byte[] emptyKey, byte[] deletedKey, byte[] retainedKey)
    {
        var stagedOwnedLength = -1;
        var stagedEmptyLength = -1;
        var stagedEmptyRead = false;
        var stagedDeleteOwned = false;
        var stagedDeleteCallback = false;
        var stagedDeleteRead = true;
        var stagedRange = new List<string>();
        store.Commit((tx, _) =>
        {
            tx.Put(emptyKey, []);
            tx.Delete(deletedKey);
            stagedOwnedLength = tx.ReadOwnedValue(emptyKey)?.Length ?? -1;
            stagedDeleteOwned = tx.ReadOwnedValue(deletedKey) is null;
            stagedEmptyRead = tx.ReadValue(emptyKey, value => stagedEmptyLength = value.Length);
            stagedDeleteRead = tx.ReadValue(deletedKey, _ => stagedDeleteCallback = true);
            tx.VisitRange(prefix, 10, (key, value) =>
            {
                stagedRange.Add($"{Convert.ToHexString(key)}:{value.Length}");
                return true;
            });
            return true;
        });

        await Assert.That(stagedOwnedLength).IsEqualTo(0);
        await Assert.That(stagedEmptyRead).IsTrue();
        await Assert.That(stagedEmptyLength).IsEqualTo(0);
        await Assert.That(stagedDeleteOwned).IsTrue();
        await Assert.That(stagedDeleteRead).IsFalse();
        await Assert.That(stagedDeleteCallback).IsFalse();
        await Assert.That(stagedRange).IsEquivalentTo(
            ["A001:0", $"{Convert.ToHexString(retainedKey)}:1"], CollectionOrdering.Matching);
    }

    private static string CreateDirectory()
    {
        var directory = Path.Combine(AppContext.BaseDirectory,
            "keyload-tombstone-values-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
