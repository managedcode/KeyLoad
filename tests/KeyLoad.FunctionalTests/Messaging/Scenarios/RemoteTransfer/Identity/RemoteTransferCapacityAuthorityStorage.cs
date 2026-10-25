using KeyLoad.Core.Messaging;
using KeyLoad.FunctionalTests.StorageRecovery.Assertions;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;
using static KeyLoad.FunctionalTests.Messaging.RemoteTransferCapacityAuthorityProtocol;

namespace KeyLoad.FunctionalTests.Messaging;

internal static class RemoteTransferCapacityAuthorityStorage
{
    internal static ZoneTreeStore Execution(RemoteTransferDatabase fixture) => (ZoneTreeStore)fixture.Store;

    internal static string[] Capture(RemoteTransferDatabase fixture) => fixture.Store.Read(view =>
    {
        var page = view.Scan([], ImageBound);
        if (page.HasMore)
        {
            throw new InvalidOperationException(RuntimeImageExceeded);
        }
        return page.Records.Select(row => Convert.ToHexString(row.Key.Span) + ":" + Convert.ToHexString(row.Value.Span)).ToArray();
    });

    internal static async Task ImageAsync(RemoteTransferDatabase fixture, string[] image, long position)
    {
        await Assert.That(fixture.Store.Position).IsEqualTo(position);
        await Assert.That(Capture(fixture)).IsEquivalentTo(image, CollectionOrdering.Matching);
    }

    internal static async Task IdentityAsync(RemoteTransferDatabase fixture, StoreIdentity identity, long position)
        => await ReadCutStoreStateAssertions.AssertIdentityAndPositionAsync(Execution(fixture), identity, position);

    internal static RemoteTransferCapacityAuthorityDamage Damage(RemoteTransferDatabase fixture, bool source,
        RemoteTransferCapacityAuthorityFault fault)
    {
        var lane = source ? fixture.SourceQueue : fixture.DestinationQueue;
        var key = source ? RemoteTransferStorage.SourceCapacityKey(lane) : RemoteTransferStorage.TargetCapacityKey(lane);
        var prefix = KeySpace.Partition(source ? RemoteTransferProtocol.IntentSpace : RemoteTransferProtocol.TargetReceiptSpace,
            lane.Partition, lane.Queue);
        return fixture.Store.Commit((tx, _) =>
        {
            var counter = tx.ReadOwnedValue(key) ?? throw new InvalidOperationException(RuntimeMissingRow);
            var page = tx.Scan(prefix, ImageBound);
            if (page.HasMore || page.Records.Length < SecondValue)
            {
                throw new InvalidOperationException(RuntimeImageExceeded);
            }
            var rows = page.Records.Select(row => new KeyValuePair<byte[], byte[]>(row.Key.ToArray(), row.Value.ToArray())).ToList();
            rows.Add(new(key, counter));
            var value = OrleansSerialization.Deserialize<RemoteTransferCapacity>(counter);
            ApplyDamage(tx, key, value, rows, fault);
            return new RemoteTransferCapacityAuthorityDamage(key, rows);
        });
    }

    private static void ApplyDamage(IAtomicTransaction tx, byte[] key, RemoteTransferCapacity value,
        IReadOnlyList<KeyValuePair<byte[], byte[]>> rows, RemoteTransferCapacityAuthorityFault fault)
    {
        if (fault == RemoteTransferCapacityAuthorityFault.Missing)
        {
            tx.Delete(key);
        }
        else if (fault == RemoteTransferCapacityAuthorityFault.MissingRecords)
        {
            foreach (var row in rows.Where(row => !row.Key.AsSpan().SequenceEqual(key)))
            {
                tx.Delete(row.Key);
            }
        }
        else
        {
            tx.PutRecord(key, fault switch
            {
                RemoteTransferCapacityAuthorityFault.Empty => new RemoteTransferCapacity(NoUsage, NoUsage),
                RemoteTransferCapacityAuthorityFault.UndersizedBytes => value with { StoredBytes = First },
                RemoteTransferCapacityAuthorityFault.UndersizedCount => value with { StoredRecords = First },
                _ => throw new InvalidOperationException(RuntimeMissingRow)
            });
        }
    }

    internal static void Repair(RemoteTransferDatabase fixture, RemoteTransferCapacityAuthorityDamage damage)
        => fixture.Store.Commit((tx, _) =>
        {
            foreach (var row in damage.OriginalRows)
            {
                tx.Put(row.Key, row.Value);
            }
            return true;
        });
}
