using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ResourceExecution;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueVisibilityBoundaryAssertions
{
    internal static (long Position, byte[] Identity, string[] Bytes) Capture(ZoneTreeStore store)
        => (store.Position, NativeSerialization.Serialize(store.Identity), QueueWholeFlowStorage.Bytes(store));

    internal static async Task SameAsync(ZoneTreeStore store, (long Position, byte[] Identity, string[] Bytes) cut)
    {
        await Assert.That(store.Position).IsEqualTo(cut.Position);
        await Assert.That(NativeSerialization.Serialize(store.Identity).AsSpan().SequenceEqual(cut.Identity)).IsTrue();
        await Assert.That(QueueWholeFlowStorage.Bytes(store)).IsEquivalentTo(cut.Bytes, CollectionOrdering.Matching);
    }

    internal static async Task RefuseAsync(DatabaseEngine owner, ZoneTreeStore store, QueueLaneRef lane,
        Delivery original, DateTimeOffset time, ErrorCode expected, string detail,
        List<(ReplicatedOperation Operation, OperationResult Result)> outcomes, string? activeToken = null)
    {
        foreach (var action in new[] { DeliveryAction.Ack, DeliveryAction.Renew })
        {
            var before = QueueLifecycleImage.Capture(store, lane);
            var id = Guid.NewGuid();
            var command = new DeliveryCommand(id, lane, original.Token, action, LeaseSeconds: 1);
            var result = QueueVisibilityBoundaryWholeFlow.Submit(owner, OperationKind.Delivery, command, id, time, outcomes);
            await Assert.That(result.Error).IsEqualTo(expected);
            await Assert.That(result.SafeDetail).IsEqualTo(detail);
            await Assert.That(result.Json).IsNull();
            await Assert.That(result.NativeValue).IsNull();
            await QueueLifecycleImage.SameAsync(store, lane, before);
            await ReplayAsync(owner, store, outcomes, activeToken);
        }
    }

    internal static async Task ReplayAsync(DatabaseEngine owner, ZoneTreeStore store,
        List<(ReplicatedOperation Operation, OperationResult Result)> outcomes, string? activeToken = null)
    {
        var cut = Capture(store);
        foreach (var original in outcomes)
        {
            if (original.Operation.Kind == OperationKind.Receive && original.Result.Error is null)
            {
                var request = JsonSerializer.Deserialize<ReceiveRequest>(original.Operation.PayloadJson, JsonDefaults.Options)
                    ?? throw new InvalidOperationException("The original Receive request is absent.");
                var current = original.Result.Get<ReceiveResult>().Deliveries.All(item => item.Token == activeToken);
                await QueueReceiveReplayAssertions.SameAsync(store, request.Lane.Partition, original.Operation,
                    original.Result, current, () => owner.Apply(original.Operation));
                await SameAsync(store, cut);
                continue;
            }
            var actual = owner.Apply(original.Operation);
            if (original.Result.Error is not null)
            {
                await Assert.That(NativeSerialization.Serialize(actual))
                    .IsEquivalentTo(NativeSerialization.Serialize(original.Result), CollectionOrdering.Matching);
                await Assert.That(actual.Error).IsEqualTo(original.Result.Error);
                await Assert.That(actual.SafeDetail).IsEqualTo(original.Result.SafeDetail);
                await Assert.That(actual.Json).IsEqualTo(original.Result.Json);
                await Assert.That(actual.NativeValue).IsNull();
            }
            else
            {
                await NativeReplayResultAssertions.Same<CommitReceipt>(actual, original.Result);
            }
            await SameAsync(store, cut);
        }
    }

    internal static async Task MessageAsync(DatabaseEngine owner, QueueLaneRef lane,
        MessageMetadata expected, bool stored, byte[]? originalBody = null)
    {
        var actual = owner.InspectMessage("root", lane, expected.Id)
            ?? throw new InvalidOperationException("The boundary message metadata is absent.");
        await Assert.That(JsonSerializer.Serialize(actual.Metadata, JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(expected, JsonDefaults.Options));
        await Assert.That(actual.PayloadJson).IsEqualTo(stored ? "{}" : null);
        await Assert.That(actual.HeadersJson).IsEqualTo(stored ? "{}" : null);
        var body = owner.Store.Read(view => view.ReadOwnedValue(KeySpace.Partition("message-body", lane.Partition, lane.Queue, expected.Id)));
        if (stored)
        {
            var message = new EnqueueMessage(lane.Queue, expected.Id, "{}");
            var literal = new MessageBody(expected.Id, "{}", "{}", null, JsonData.Fingerprint(message));
            var persisted = body ?? throw new InvalidOperationException("The complete boundary message body is absent.");
            await Assert.That(NativeSerialization.Deserialize<MessageBody>(persisted)).IsEqualTo(literal);
            var saved = originalBody ?? throw new InvalidOperationException("The original producer-persisted body is absent.");
            await Assert.That(persisted.AsSpan().SequenceEqual(saved)).IsTrue();
        }
        else
        {
            await Assert.That(body).IsNull();
        }
    }

    internal static async Task DeliveryReceiptAsync((ReplicatedOperation Operation, OperationResult Result) original,
        DeliveryAction action, string message, long version)
    {
        var command = JsonSerializer.Deserialize<DeliveryCommand>(original.Operation.PayloadJson, JsonDefaults.Options)
            ?? throw new InvalidOperationException("The original delivery command is absent.");
        var receipt = original.Result.Get<CommitReceipt>();
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Mutations).IsEquivalentTo(
            new MutationReceipt[] { new(action.ToString(), command.Lane.Queue, message, version) }, CollectionOrdering.Matching);
    }

    internal static async Task TerminalAsync(DatabaseEngine owner, ZoneTreeStore store, QueueLaneRef lane, byte[] originalRow)
    {
        var cut = Capture(store);
        await MessageAsync(owner, lane, new(QueueVisibilityBoundaryWholeFlow.Message,
            MessageState.Acked, 2, 7, 4, null, null, LeaseVersion: 2), stored: false);
        await MessageAsync(owner, lane, new(QueueVisibilityBoundaryWholeFlow.Healthy,
            MessageState.Acked, 1, 3, 5, null, null, LeaseVersion: 1), stored: false);
        var preserved = store.Read(view => view.ReadOwnedValue(KeySpace.Partition("message-meta", lane.Partition, lane.Queue, "input")));
        await Assert.That(preserved is not null && preserved.AsSpan().SequenceEqual(originalRow)).IsTrue();
        var counters = store.Read(view => view.ReadOwnedValue(KeySpace.Partition("queue-counters", lane.Partition, lane.Queue)));
        await Assert.That(counters is not null &&
            counters.AsSpan().SequenceEqual(NativeSerialization.Serialize(new QueueCounters(0, 0, 0, 0, 5)))).IsTrue();
        foreach (var family in new[] { "ready", "scheduled", "lease", "message-body", "dead-letter", "queue-pending-dead-letter", "queue-dead-letter-order", "queue-order" })
        {
            var page = store.Read(view => view.Scan(KeySpace.Partition(family, lane.Partition, lane.Queue), 1));
            await Assert.That(page.Records).IsEmpty();
            await Assert.That(page.HasMore).IsFalse();
        }
        await SameAsync(store, cut);
    }
}
