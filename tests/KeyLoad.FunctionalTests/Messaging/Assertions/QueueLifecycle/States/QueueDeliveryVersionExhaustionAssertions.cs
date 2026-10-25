using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;

namespace KeyLoad.FunctionalTests.Messaging;

internal static class QueueDeliveryVersionExhaustionAssertions
{
    private const string Detail = "The queue delivery state version is exhausted.";

    internal static async Task FailureAsync(Result<object> result)
    {
        await Assert.That(Errors.Code(result.Problem).Value).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(result.Problem?.Detail).IsEqualTo(Detail);

        await Assert.That(result.Value).IsNull();
    }

    internal static async Task LeasedAsync(DatabaseEngine database, ZoneTreeStore store,
        QueueDeliveryVersionExhaustionState state, MessageMetadata metadata)
    {
        var data = state.Data;
        await Assert.That(database.InspectMessage(QueueClaimVersionExhaustionProtocol.Root, data.Lane, data.Body.Id))
            .IsEqualTo(new MessageInspection(metadata, data.Body.PayloadJson, data.Body.HeadersJson));
        await Assert.That(store.Read(view => view.GetRecord<MessageBody>(KeySpace.Partition(QueueClaimVersionExhaustionProtocol.BodySpace,
            data.Lane.Partition, data.Lane.Queue, data.Body.Id)))).IsEqualTo(data.Body);
        var bytes = OrleansSerialization.Serialize(data.Body).LongLength;
        await Assert.That(store.Read(view => view.GetRecord<QueueCounters>(KeySpace.Partition(QueueClaimVersionExhaustionProtocol.CounterSpace,
            data.Lane.Partition, data.Lane.Queue)))).IsEqualTo(new QueueCounters(1, bytes, 1, bytes, 1));
        await Assert.That(store.Read(view => view.GetRecord<string>(KeySpace.Partition(QueueClaimVersionExhaustionProtocol.LeaseSpace,
            data.Lane.Partition, data.Lane.Queue, metadata.LeaseUntil!.Value, data.Body.Id)))).IsEqualTo(data.Body.Id);
        await Assert.That(store.Read(view => view.Scan(KeySpace.Partition(QueueClaimVersionExhaustionProtocol.ReadySpace,
            data.Lane.Partition, data.Lane.Queue), QueueClaimVersionExhaustionProtocol.ImageRecords).Records)).IsEmpty();
        if (metadata.LeaseUntil != state.Delivery.LeaseUntil)
        {
            await Assert.That(store.Read(view => view.GetRecord<string>(KeySpace.Partition(QueueClaimVersionExhaustionProtocol.LeaseSpace,
                data.Lane.Partition, data.Lane.Queue, state.Delivery.LeaseUntil, data.Body.Id)))).IsNull();
        }
    }

    internal static async Task ScheduledAsync(DatabaseEngine database, ZoneTreeStore store,
        QueueDeliveryVersionExhaustionState state, MessageMetadata metadata)
    {
        var data = state.Data;
        await Assert.That(database.InspectMessage(QueueClaimVersionExhaustionProtocol.Root, data.Lane, data.Body.Id))
            .IsEqualTo(new MessageInspection(metadata, data.Body.PayloadJson, data.Body.HeadersJson));
        var bytes = OrleansSerialization.Serialize(data.Body).LongLength;
        await Assert.That(store.Read(view => view.GetRecord<QueueCounters>(KeySpace.Partition(QueueClaimVersionExhaustionProtocol.CounterSpace,
            data.Lane.Partition, data.Lane.Queue)))).IsEqualTo(new QueueCounters(1, bytes, 0, 0, 1));
        await Assert.That(store.Read(view => view.GetRecord<string>(KeySpace.Partition(QueueClaimVersionExhaustionProtocol.ScheduledSpace,
            data.Lane.Partition, data.Lane.Queue, metadata.NotBefore!.Value, data.Body.Id)))).IsEqualTo(data.Body.Id);
        await Assert.That(store.Read(view => view.GetRecord<string>(KeySpace.Partition(QueueClaimVersionExhaustionProtocol.LeaseSpace,
            data.Lane.Partition, data.Lane.Queue, state.Delivery.LeaseUntil, data.Body.Id)))).IsNull();
    }

    internal static async Task ReceiptAsync(Result<object> result, DeliveryCommand command, string id, long revision)
    {
        var receipt = result.Get<CommitReceipt>();
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        var mutation = await Assert.That(receipt.Mutations).HasSingleItem();
        await Assert.That(mutation).IsEqualTo(new MutationReceipt(command.Action.ToString(), command.Lane.Queue, id, revision));
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(command.Lane.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
    }

    internal static async Task TerminalAsync(DatabaseEngine database, ZoneTreeStore store, QueueDeliveryVersionExhaustionState state,
        MessageMetadata metadata, bool healthy)
    {
        var data = state.Data;
        await Assert.That(database.InspectMessage(QueueClaimVersionExhaustionProtocol.Root, data.Lane, data.Body.Id))
            .IsEqualTo(new MessageInspection(metadata, null, null));
        await Assert.That(store.Read(view => view.GetRecord<QueueCounters>(KeySpace.Partition(QueueClaimVersionExhaustionProtocol.CounterSpace,
            data.Lane.Partition, data.Lane.Queue)))).IsEqualTo(new QueueCounters(0, 0, 0, 0, healthy ? metadata.ReadySequence + 1 : metadata.ReadySequence));
    }
}
