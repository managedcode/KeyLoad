using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueLifecycleConditionOperations
{
    internal const string Source = "condition-source";
    internal const string Healthy = "condition-healthy";
    private const string ForeignId = "foreign-condition-source";
    internal static QueueLifecycleTestState Seed(TestDatabase fixture)
    {
        QueueLifecycleProvisioning.Seed(fixture, byteSublimit: false);
        var state = new QueueLifecycleTestState(fixture.Partition, QueueWholeFlowStorage.Clock(fixture.Store));
        state.Batch(fixture.Database, QueueLifecycleHealthy.Enqueue(Source)).Get<CommitReceipt>();
        return state;
    }
    internal static ReplicatedOperation Cancel(QueueLifecycleTestState state, string id = Source, long version = 1, long generation = 1)
    {
        var commandId = Guid.NewGuid();
        var command = new CommandRequest(commandId, state.Partition,
            [new CancelQueueMessage(state.Lane.Queue, id, version, generation)]);
        return new(commandId, OperationKind.Batch, QueueLifecycleTestProtocol.Administrator, state.Time,
            JsonSerializer.Serialize(command, JsonDefaults.Options));
    }
    internal static byte[] Key(QueueLifecycleTestState state, QueueLifecycleConditionFault fault)
        => KeySpace.Partition(fault == QueueLifecycleConditionFault.MetadataIdentity
            ? QueueLifecycleTestProtocol.MetadataSpace : QueueLifecycleTestProtocol.BodySpace, state.Partition, state.Lane.Queue, Source);
    internal static byte[] Damage(ZoneTreeStore store, QueueLifecycleTestState state, QueueLifecycleConditionFault fault)
    {
        var key = Key(state, fault);
        var original = store.Read(view => view.ReadOwnedValue(key))!;
        var bytes = fault == QueueLifecycleConditionFault.MetadataIdentity
            ? NativeSerialization.Serialize(NativeSerialization.Deserialize<MessageMetadata>(original) with { Id = ForeignId })
            : NativeSerialization.Serialize(NativeSerialization.Deserialize<MessageBody>(original) with { Id = ForeignId });
        Restore(store, key, bytes);
        return original;
    }
    internal static void Restore(ZoneTreeStore store, byte[] key, byte[] bytes)
        => store.Commit((tx, _) => { tx.Put(key, bytes); return true; });
    internal static async Task HealthyWork(DatabaseEngine owner, QueueLifecycleTestState state)
    {
        state.Batch(owner, QueueLifecycleHealthy.Enqueue(Healthy)).Get<CommitReceipt>();
        var delivery = await QueueLifecycleOperations.ClaimAsync(owner, state, Healthy);
        QueueLifecycleOperations.Complete(owner, state, delivery, DeliveryAction.Ack).Get<CommitReceipt>();
    }
}
