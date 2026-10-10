using KeyLoad.Core.Features.Messaging;
using KeyLoad.CrashHost;
using KeyLoad.CrashHost.Features.Messaging;
using KeyLoad.Storage.ZoneTree;
namespace KeyLoad.RecoveryTests.Features.Messaging;

internal static class TargetInboxRecoveryCold
{
    internal static async Task VerifyAsync(string root, ReplicatedOperation operation, CommitInboxRequest request,
        CommitInboxResult original, CancellationToken token)
    {
        using var store = new ZoneTreeStore(new(root), RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution());
        var database = TargetInboxRecoveryDatabase.Open(store);
        var position = store.Position;
        var before = Image(store);
        var replay = database.ApplyEmbedded(operation, token).Get<CommitInboxResult>();
        await Assert.That(JsonDefaults.Serialize(replay).AsSpan().SequenceEqual(JsonDefaults.Serialize(original))).IsTrue();
        await Assert.That(Image(store).SequenceEqual(before)).IsTrue();
        await Assert.That(store.Position).IsEqualTo(position);
        await Assert.That(database.InspectMessage(CrashFixtureValues.Principal, request.Source, request.MessageId)!.Metadata.State).IsEqualTo(MessageState.Acked);
        var queue = new QueueLaneRef(request.Target.Partition, TargetInboxCrashProtocol.Output);
        var receive = new ReceiveRequest(Guid.NewGuid(), queue);
        var delivery = database.ApplyEmbedded(new(receive.RequestId, OperationKind.Receive, CrashFixtureValues.Principal, default,
            System.Text.Json.JsonSerializer.Serialize(receive, JsonDefaults.Options)), token).Get<ReceiveResult>().Deliveries.Single();
        var ack = new DeliveryCommand(Guid.NewGuid(), queue, delivery.Token, DeliveryAction.Ack);
        _ = database.ApplyEmbedded(new(ack.CommandId, OperationKind.Delivery, CrashFixtureValues.Principal, default,
            System.Text.Json.JsonSerializer.Serialize(ack, JsonDefaults.Options)), token).Get<CommitReceipt>();
        await Assert.That(database.InspectMessage(CrashFixtureValues.Principal, queue, delivery.Id)!.Metadata.State).IsEqualTo(MessageState.Acked);
        await Assert.That(store.Read(view => global::KeyLoad.Storage.StorageRecords.GetRecord<TargetInboxCapacity>(view, TargetInboxStorage.CapacityKey(request.Target)))!.Count).IsEqualTo(TargetInboxCrashProtocol.Generation);
    }
    private static string[] Image(ZoneTreeStore store) => store.Read(view =>
    {
        const int MaximumRows = 4096;
        var page = view.Scan([], MaximumRows);
        if (page.HasMore)
        { throw new InvalidOperationException("Target inbox recovery exceeded its fixture row bound."); }
        return page.Records.Select(row => Convert.ToHexString(row.Key.Span) + ":" + Convert.ToHexString(row.Value.Span)).ToArray();
    });
}
