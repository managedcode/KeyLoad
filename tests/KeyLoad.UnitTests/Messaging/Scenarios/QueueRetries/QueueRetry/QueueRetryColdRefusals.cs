using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueRetryColdRefusals
{
    internal static async Task RunAsync(DatabaseEngine database, QueueRetryColdState state, CancellationToken token)
    {
        var time = state.Start.AddMilliseconds(QueueRetryColdProtocol.SecondDueMilliseconds);
        var image = QueueRetryColdAssertions.LaneBytes((ZoneTreeStore)database.Store, state.Lane);
        var producer = new CommandRequest(Guid.NewGuid(), state.Lane.Partition,
            [new PutDocument(QueueRetryColdProtocol.Collection, QueueRetryColdProtocol.Refused, QueueRetryColdProtocol.HealthyPayload),
             new EnqueueMessage(state.Lane.Queue, QueueRetryColdProtocol.Healthy, QueueRetryColdProtocol.HealthyPayload)]);
        var quota = QueueWholeFlowStorage.Apply(database, OperationKind.Batch, producer, producer.CommandId, time);
        state.Rejected = (producer, quota);
        await Assert.That(quota.Error).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(quota.NativeValue).IsNull();
        await Assert.That(database.GetDocument(QueueRetryColdProtocol.Root, new(state.Lane.Partition,
            QueueRetryColdProtocol.Collection, QueueRetryColdProtocol.Refused), cancellationToken: token)).IsNull();
        await Assert.That(QueueRetryColdAssertions.LaneBytes((ZoneTreeStore)database.Store, state.Lane)).IsEquivalentTo(image, CollectionOrdering.Matching);
        foreach (var action in new[] { DeliveryAction.Ack, DeliveryAction.Renew })
        {
            var command = new DeliveryCommand(Guid.NewGuid(), state.Lane, state.FirstDelivery.Token, action);
            var actual = QueueWholeFlowStorage.Apply(database, OperationKind.Delivery, command, command.CommandId, time);
            await Assert.That(actual.Error).IsEqualTo(ErrorCode.StaleLease);
            await Assert.That(actual.NativeValue).IsNull();
            await Assert.That(QueueRetryColdAssertions.LaneBytes((ZoneTreeStore)database.Store, state.Lane)).IsEquivalentTo(image, CollectionOrdering.Matching);
        }
        var deniedInspection = Assert.ThrowsExactly<KeyLoadException>(() => database.InspectMessage(QueueRetryColdProtocol.Inspector,
            state.Lane, QueueRetryColdProtocol.Retry));
        await Assert.That(deniedInspection.Code).IsEqualTo(ErrorCode.PermissionDenied);
        var receive = new ReceiveRequest(Guid.NewGuid(), state.Lane);
        var denied = database.Apply(new(receive.RequestId, OperationKind.Receive, QueueRetryColdProtocol.Inspector,
            time, JsonSerializer.Serialize(receive, JsonDefaults.Options)));
        await Assert.That(denied.Error).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(denied.NativeValue).IsNull();
        await Assert.That(QueueRetryColdAssertions.LaneBytes((ZoneTreeStore)database.Store, state.Lane)).IsEquivalentTo(image, CollectionOrdering.Matching);
        await CancelledAsync(database, state);
        await QueueRetryColdAssertions.TerminalAsync(database, state, false);
    }

    private static async Task CancelledAsync(DatabaseEngine database, QueueRetryColdState state)
    {
        var image = QueueWholeFlowStorage.Bytes((ZoneTreeStore)database.Store);
        var position = database.Store.Position;
        var request = new ReceiveRequest(Guid.NewGuid(), state.Lane);
        var cancelled = new CancellationToken(canceled: true);
        var failure = Assert.ThrowsExactly<OperationCanceledException>(() => database.ApplyEmbedded(new(request.RequestId,
            OperationKind.Receive, QueueRetryColdProtocol.Root, default, JsonSerializer.Serialize(request, JsonDefaults.Options)), cancelled));
        await Assert.That(failure.CancellationToken).IsEqualTo(cancelled);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes((ZoneTreeStore)database.Store)).IsEquivalentTo(image, CollectionOrdering.Matching);
    }

    internal static async Task StaleClaimReplayAsync(DatabaseEngine database, QueueRetryColdState state)
    {
        var key = KeySpace.PartitionOutcome(state.Lane.Partition, QueueRetryColdProtocol.Root, state.FirstClaim.RequestId);
        var original = database.Store.Read(view => view.ReadOwnedValue(key)!);
        var image = QueueRetryColdAssertions.LaneBytes((ZoneTreeStore)database.Store, state.Lane);
        var actual = QueueWholeFlowStorage.Apply(database, OperationKind.Receive, state.FirstClaim,
            state.FirstClaim.RequestId, state.FinalTime);
        await Assert.That(actual.Error).IsEqualTo(ErrorCode.StaleLease);
        await Assert.That(actual.NativeValue).IsNull();
        await Assert.That(database.Store.Read(view => view.ReadOwnedValue(key)!)).IsEquivalentTo(original, CollectionOrdering.Matching);
        await Assert.That(QueueRetryColdAssertions.LaneBytes((ZoneTreeStore)database.Store, state.Lane)).IsEquivalentTo(image, CollectionOrdering.Matching);
    }
}
