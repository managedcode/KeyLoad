using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using Microsoft.Extensions.Options;
using static KeyLoad.UnitTests.Features.ResourceExecution.ResourceRetirementCleanupNativeData;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class ResourceRetirementCleanupNativePages
{
    internal static ResourceRetirementTableCleanupProgress Commit(ZoneTreeStore store,
        ResourceRetirementCleanupNativeState state, PartitionRef partition, DatabaseLimits limits, bool rollback = false)
        => store.Commit((transaction, _) =>
        {
            var progress = ResourceRetirementTableCleanupPage.Apply(transaction, state.Lifetime, partition,
                Cursor(transaction, partition), Options.Create(limits),
                (limits.MaxQueryReadBytes, limits.MaxScanRecords), ReservedMutations, ReservedBytes);
            var value = KeyCodec.Encode(progress.Next.FamilyOrdinal, progress.Next.AfterKey);
            if (Checkpoint(partition).LongLength + value.LongLength > ReservedBytes)
            {
                throw new InvalidOperationException(PageBound);
            }

            transaction.Put(Checkpoint(partition), value);
            if (rollback)
            {
                throw new InvalidOperationException(Rollback);
            }

            return progress;
        });

    internal static async Task CompleteAsync(ZoneTreeStore store, ResourceRetirementCleanupNativeState state,
        PartitionRef partition, DatabaseLimits limits, CancellationToken token)
    {
        for (var ordinal = Zero; ordinal < MaximumPages; ordinal++)
        {
            token.ThrowIfCancellationRequested();
            var progress = await CommitCheckedAsync(store, state, partition, limits);
            if (!progress.HasMore)
            {
                return;
            }
        }
        throw new InvalidOperationException(PageBound);
    }

    internal static async Task<ResourceRetirementTableCleanupProgress> CommitCheckedAsync(ZoneTreeStore store,
        ResourceRetirementCleanupNativeState state, PartitionRef partition, DatabaseLimits limits)
    {
        var before = ResourceRetirementCleanupNativeImages.Read(store);
        var position = store.Position;
        var progress = Commit(store, state, partition, limits);
        await DeltaAsync(store, before, position, state, partition, progress);
        return progress;
    }

    private static ResourceRetirementTableCleanupCursor Cursor(IKeyValueView view, PartitionRef partition)
    {
        var bytes = view.ReadOwnedValue(Checkpoint(partition));
        if (bytes is null)
        {
            return new(Zero);
        }

        var fields = KeyCodec.Decode(bytes);
        return new(checked((int)(long)fields[FirstIndex]!), (byte[])fields[SecondIndex]!);
    }

    private static async Task DeltaAsync(ZoneTreeStore store, Dictionary<string, string> before, long position,
        ResourceRetirementCleanupNativeState state, PartitionRef partition, ResourceRetirementTableCleanupProgress progress)
    {
        var after = ResourceRetirementCleanupNativeImages.Read(store);
        var removed = before.Keys.Where(key => !after.ContainsKey(key)).ToArray();
        await Assert.That(removed.Length).IsEqualTo(progress.DeletedRecords);
        await Assert.That(removed.Length <= One).IsTrue();
        var scope = Families.Select(family => Convert.ToHexString(KeySpace.Partition(family, partition, Table))).ToArray();
        foreach (var key in removed)
        {
            await Assert.That(state.Owned.Contains(key)).IsTrue();
            await Assert.That(scope.Any(prefix => key.StartsWith(prefix, StringComparison.Ordinal))).IsTrue();
        }
        var expected = before.Where(row => !removed.Contains(row.Key, StringComparer.Ordinal))
            .ToDictionary(row => row.Key, row => row.Value, StringComparer.Ordinal);
        expected[Convert.ToHexString(Checkpoint(partition))] =
            Convert.ToHexString(KeyCodec.Encode(progress.Next.FamilyOrdinal, progress.Next.AfterKey));
        await ResourceRetirementCleanupNativeImages.SameAsync(store, expected, checked(position + One));
        var deleted = removed.Sum(key => (long)(key.Length + before[key].Length) / HexCharactersPerByte);
        await Assert.That(progress.DeletedBytes).IsEqualTo(deleted);
        await Assert.That(progress.ExaminedBytes >= progress.DeletedBytes).IsTrue();
    }
}
