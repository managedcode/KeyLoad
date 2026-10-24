using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Messaging;

internal static class QueueReadyAuthorityFaultRow
{
    internal static (byte[] Key, byte[] Original) Damage(TestDatabase fixture,
        QueueLaneRef lane, QueueReadyAuthorityFault fault)
        => fixture.Store.Commit((tx, _) =>
        {
            var body = fault == QueueReadyAuthorityFault.BodyIdentity;
            var key = KeySpace.Partition(body ? "message-body" : "message-meta", lane.Partition, lane.Queue, "input");
            var original = tx.ReadOwnedValue(key) ?? throw new InvalidOperationException("The original execution fault-owned row is absent.");
            if (body)
            {
                tx.PutRecord(key, OrleansSerialization.Deserialize<MessageBody>(original) with
                {
                    Id = "foreign"
                });
            }
            else
            {
                var metadata = OrleansSerialization.Deserialize<MessageMetadata>(original);
                var changed = fault switch
                {
                    QueueReadyAuthorityFault.ScheduledState => metadata with { State = MessageState.Scheduled },
                    QueueReadyAuthorityFault.ReadySequence => metadata with { ReadySequence = metadata.ReadySequence + 1 },
                    QueueReadyAuthorityFault.MetadataIdentity => metadata with { Id = "foreign" },
                    QueueReadyAuthorityFault.NegativeAttempts => metadata with { Attempts = -1 },
                    QueueReadyAuthorityFault.NonPositiveStateVersion => metadata with { StateVersion = 0 },
                    QueueReadyAuthorityFault.NegativeLeaseVersion => metadata with { LeaseVersion = -1 },
                    QueueReadyAuthorityFault.NonPositiveDeliveryGeneration => metadata with { DeliveryGeneration = 0 },
                    _ => throw new InvalidOperationException("The execution ready authority fault is invalid.")
                };
                tx.PutRecord(key, changed);
            }
            return (key, original);
        });

    internal static void Restore(TestDatabase fixture, byte[] key, byte[] original)
        => fixture.Store.Commit((tx, _) => { tx.Put(key, original); return true; });
}
