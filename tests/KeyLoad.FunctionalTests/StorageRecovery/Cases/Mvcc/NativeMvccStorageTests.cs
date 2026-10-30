using KeyLoad.Storage.ZoneTree;
using ZoneTree.Transactional;

namespace KeyLoad.FunctionalTests.StorageRecovery;

internal sealed class NativeMvccStorageTests
{
    [Test]
    public async Task NativeCommitAndRollbackKeepTheCapturedSnapshotAndLiteralValuesExact()
    {
        using var files = new WalFileFixture();
        using var tree = ZoneTreeTreeFactory.Open(new(files.DirectoryPath));
        Memory<byte> key = new byte[] { 0x10 };
        Memory<byte> value = new byte[] { 0x00, 0xFF, 0x80 };
        using var before = tree.BeginReadOnlyTransaction();
        using (var writer = tree.BeginTransaction())
        {
            writer.Upsert(key, value);
            await Assert.That(before.TryGet(key, out _)).IsFalse();
            using var uncommitted = tree.BeginReadOnlyTransaction();
            await Assert.That(uncommitted.TryGet(key, out _)).IsFalse();
            writer.Commit();
            await Assert.That(uncommitted.TryGet(key, out _)).IsFalse();
        }
        await Assert.That(before.TryGet(key, out _)).IsFalse();
        using var committed = tree.BeginReadOnlyTransaction();
        await Assert.That(committed.TryGet(key, out var stored)).IsTrue();
        await Assert.That(stored.Span.SequenceEqual(value.Span)).IsTrue();

        using (var canceled = tree.BeginTransaction())
        {
            canceled.Delete(key);
            canceled.Upsert(new byte[] { 0x20 }, Memory<byte>.Empty);
        }
        using var afterRollback = tree.BeginReadOnlyTransaction();
        await Assert.That(Rows(afterRollback)).IsEquivalentTo(new[] { "10=00FF80" },
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        using (var healthy = tree.BeginTransaction())
        {
            healthy.Delete(key);
            healthy.Upsert(new byte[] { 0x20 }, Memory<byte>.Empty);
            healthy.Commit();
        }
        await Assert.That(Rows(committed)).IsEquivalentTo(new[] { "10=00FF80" },
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        using var final = tree.BeginReadOnlyTransaction();
        await Assert.That(Rows(final)).IsEquivalentTo(new[] { "20=" },
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task SerializableRangeConflictRollsBackEveryWriteThenHealthyCommitSucceeds()
    {
        using var files = new WalFileFixture();
        using var tree = ZoneTreeTreeFactory.Open(new(files.DirectoryPath));
        using (var seed = tree.BeginTransaction())
        {
            seed.Upsert(new byte[] { 0x10 }, new byte[] { 0xA0 });
            seed.Commit();
        }
        using var contender = tree.BeginTransaction(TransactionIsolationLevel.Serializable);
        await Assert.That(contender.Range(new byte[] { 0x10 }, new byte[] { 0x30 }).Count()).IsEqualTo(1);
        using (var concurrent = tree.BeginTransaction())
        {
            concurrent.Upsert(new byte[] { 0x20 }, new byte[] { 0xB0 });
            concurrent.Commit();
        }
        contender.Upsert(new byte[] { 0x40 }, new byte[] { 0xC0 });
        await Assert.That(contender.TryCommit()).IsFalse();
        using (var healthy = tree.BeginTransaction())
        {
            healthy.Upsert(new byte[] { 0x50 }, new byte[] { 0xD0 });
            healthy.Commit();
        }
        using var final = tree.BeginReadOnlyTransaction();
        await Assert.That(Rows(final)).IsEquivalentTo(new[] { "10=A0", "20=B0", "50=D0" },
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    private static string[] Rows(IReadTransaction<Memory<byte>, Memory<byte>> transaction)
    {
        using var iterator = transaction.CreateIterator();
        var rows = new List<string>();
        while (iterator.Next())
        {
            rows.Add($"{Convert.ToHexString(iterator.CurrentKey.Span)}={Convert.ToHexString(iterator.CurrentValue.Span)}");
        }
        return rows.ToArray();
    }
}
