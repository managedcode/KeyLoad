using KeyLoad.Storage.ZoneTree;
namespace KeyLoad.UnitTests.Messaging;

internal static class TargetInboxQuota
{
    internal static async Task ContinueAsync(DatabaseEngine database, ZoneTreeStore store, CommitInboxRequest original,
        CancellationToken token)
    {
        var second = original with
        {
            CommandId = Guid.NewGuid(),
            ExecutionGeneration = TargetInboxUnitProtocol.ReceiptCapacity,
            Effects = []
        };
        var actual = TargetInboxSetup.Apply(database, second, token).Get<CommitInboxResult>();
        await Assert.That(actual.AlreadyProcessed).IsFalse();
        var third = second with
        {
            CommandId = Guid.NewGuid(),
            ExecutionGeneration = checked(TargetInboxUnitProtocol.ReceiptCapacity + TargetInboxUnitProtocol.FirstRevision),
            Effects = [new PutDocument(TargetInboxUnitProtocol.Collection, TargetInboxUnitProtocol.Changed, TargetInboxUnitProtocol.Payload)]
        };
        await TargetInboxAssertions.RefusedAsync(database, store, third, ErrorCode.ResourceExhausted, token);
        await Assert.That(database.GetDocument(TargetInboxUnitProtocol.Root,
            new(original.Target.Partition, TargetInboxUnitProtocol.Collection, TargetInboxUnitProtocol.Changed), cancellationToken: token)).IsNull();
        var replay = TargetInboxSetup.Apply(database, original with
        {
            CommandId = Guid.NewGuid()
        }, token).Get<CommitInboxResult>();
        await Assert.That(replay.AlreadyProcessed).IsTrue();
        await TargetInboxAssertions.CapacityAsync(store, original, TargetInboxUnitProtocol.ReceiptCapacity);
        await TargetInboxAssertions.EffectsAsync(database, original);
        var output = new QueueLaneRef(original.Target.Partition, TargetInboxUnitProtocol.Output);
        var receive = new ReceiveRequest(Guid.NewGuid(), output);
        var delivery = database.ApplyEmbedded(database.CreateCommand(OperationKind.Receive, receive.RequestId, TargetInboxUnitProtocol.Root, default, System.Text.Json.JsonSerializer.Serialize(receive, JsonDefaults.Options)), token).Get<ReceiveResult>().Deliveries.Single();
        var ack = new DeliveryCommand(Guid.NewGuid(), output, delivery.Token, DeliveryAction.Ack);
        _ = database.ApplyEmbedded(database.CreateCommand(OperationKind.Delivery, ack.CommandId, TargetInboxUnitProtocol.Root, default, System.Text.Json.JsonSerializer.Serialize(ack, JsonDefaults.Options)), token).Get<CommitReceipt>();
        await Assert.That(database.InspectMessage(TargetInboxUnitProtocol.Root, output,
            TargetInboxUnitProtocol.Message)!.Metadata.State).IsEqualTo(MessageState.Acked);
        await TargetInboxAssertions.CapacityAsync(store, original, TargetInboxUnitProtocol.ReceiptCapacity);
    }
}
