using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class SubscriptionParkedRecoveryAssertions
{
    internal static async Task StatusAsync(DatabaseEngine db, SubscriptionRef group, SubscriptionPolicy policy,
        long generation, long checkpoint, long issued, long tail, bool paused, string? failure)
    {
        var expected = new SubscriptionInfo(group, new("root") { Policy = policy },
            generation, generation, checkpoint, issued, tail, paused, failure);
        await Assert.That(JsonSerializer.Serialize(db.GetSubscription("root", group), JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(expected, JsonDefaults.Options));
    }

    internal static async Task ResumeRefusalAsync(DatabaseEngine db, ZoneTreeStore store,
        SubscriptionRef group, DateTimeOffset time,
        List<(ReplicatedOperation Operation, OperationResult Result, byte[] Saved)> receipts)
    {
        var request = new SetSubscriptionPausedRequest(Guid.NewGuid(), group, 1, false);
        var operation = new ReplicatedOperation(request.CommandId, OperationKind.SetSubscriptionPaused,
            "root", time, JsonSerializer.Serialize(request, JsonDefaults.Options));
        var before = SubscriptionProcessingAtomicAssertions.DomainImage(store, group, operation.Id);
        var position = store.Position;
        var failure = db.Apply(operation);
        await Assert.That(failure.Error).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(failure.SafeDetail).IsEqualTo("A parked subscription requires an explicit seek or new group before resuming.");
        await Assert.That(failure.Json).IsNull();
        await Assert.That(failure.NativeValue).IsNull();
        await SubscriptionProcessingAtomicAssertions.OwnOutcomeOnlyAsync(store, group, operation, before, position + 1);
        var saved = SubscriptionProcessingAtomicAssertions.OutcomeBytes(store, group, operation.Id);
        var cut = QueueWholeFlowStorage.Bytes(store);
        position = store.Position;
        var identity = NativeSerialization.Serialize(store.Identity);
        var replay = db.Apply(operation);
        await Assert.That(JsonSerializer.Serialize(replay, JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(failure, JsonDefaults.Options));
        await Assert.That(SubscriptionProcessingAtomicAssertions.OutcomeBytes(store, group, operation.Id)
            .AsSpan().SequenceEqual(saved)).IsTrue();
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(store, cut, position, identity);
        receipts.Add((operation, failure, saved));
    }

    internal static async Task ReplayAsync(DatabaseEngine db, ZoneTreeStore store, SubscriptionRef group,
        ReplicatedOperation operation, OperationResult original, byte[] saved)
    {
        var cut = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var identity = NativeSerialization.Serialize(store.Identity);
        var replay = db.Apply(operation);
        if (original.Error is not null)
        {
            await Assert.That(replay.Error).IsEqualTo(original.Error);
            await Assert.That(replay.SafeDetail).IsEqualTo(original.SafeDetail);
            await Assert.That(replay.Json).IsNull();
            await Assert.That(replay.NativeValue).IsNull();
        }
        else if (original.NativeValue is CommitReceipt)
        { await NativeReplayResultAssertions.Same<CommitReceipt>(replay, original); }
        else if (original.NativeValue is SubscriptionInfo)
        { await NativeReplayResultAssertions.Same<SubscriptionInfo>(replay, original); }
        else
        { await NativeReplayResultAssertions.Same<ReceiveSubscriptionResult>(replay, original); }
        await Assert.That(SubscriptionProcessingAtomicAssertions.OutcomeBytes(store, group, operation.Id)
            .AsSpan().SequenceEqual(saved)).IsTrue();
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(store, cut, position, identity);
    }

    internal static async Task NoWindowAsync(ZoneTreeStore store, SubscriptionRef group)
    {
        foreach (var generation in new long[] { 1, 2 })
        {
            var prefix = KeySpace.Partition("subscription-window", group.Source.Partition, group.Source.Resource,
                group.Source.Kind.ToString(), group.Source.StreamId, group.Source.Generation, group.GroupId, generation);
            var page = store.Read(view => view.Scan(prefix, 4));
            await Assert.That(page.Records).IsEmpty();
            await Assert.That(page.HasMore).IsFalse();
        }
    }

    internal static async Task FencedAsync(DatabaseEngine db, ZoneTreeStore store,
        SubscriptionRef group, string oldToken, DateTimeOffset time)
    {
        var command = new SubscriptionDeliveryCommand(Guid.NewGuid(), group, oldToken, DeliveryAction.Ack);
        var operation = new ReplicatedOperation(command.CommandId, OperationKind.SubscriptionDelivery,
            "root", time, JsonSerializer.Serialize(command, JsonDefaults.Options));
        var before = SubscriptionProcessingAtomicAssertions.DomainImage(store, group, command.CommandId);
        var position = store.Position;
        var failure = db.Apply(operation);
        await Assert.That(failure.Error).IsEqualTo(ErrorCode.TokenInvalidated);
        await Assert.That(failure.SafeDetail).IsEqualTo("The subscription token scope or generation is invalid.");
        await Assert.That(failure.Json).IsNull();
        await Assert.That(failure.NativeValue).IsNull();
        await SubscriptionProcessingAtomicAssertions.OwnOutcomeOnlyAsync(store, group, operation, before, position + 1);
    }

    internal static async Task OldReceiveAsync(DatabaseEngine db, ZoneTreeStore store, SubscriptionRef group,
        ReplicatedOperation operation, OperationResult original, byte[] saved)
    {
        var cut = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var identity = NativeSerialization.Serialize(store.Identity);
        var result = db.Apply(operation);
        await Assert.That(result.Error).IsEqualTo(ErrorCode.TokenInvalidated);
        await Assert.That(result.SafeDetail).IsEqualTo("The cached receive belongs to an earlier subscription generation.");
        await Assert.That(result.Json).IsNull();
        await Assert.That(result.NativeValue).IsNull();
        var retained = SubscriptionProcessingAtomicAssertions.OutcomeBytes(store, group, operation.Id);
        await Assert.That(retained.AsSpan().SequenceEqual(saved)).IsTrue();
        await NativeReplayResultAssertions.Same<ReceiveSubscriptionResult>(
            NativeSerialization.Deserialize<StoredOutcome>(retained).Result, original);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(store, cut, position, identity);
    }
}

