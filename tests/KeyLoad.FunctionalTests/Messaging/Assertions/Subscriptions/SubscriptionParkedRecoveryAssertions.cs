using System.Text.Json;
using KeyLoad.FunctionalTests.ResourceExecution;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.FunctionalTests.Messaging;

internal static class SubscriptionParkedRecoveryAssertions
{
    internal static async Task StatusAsync(DatabaseEngine db, SubscriptionRef group, SubscriptionPolicy policy,
        long generation, long checkpoint, long issued, long tail, bool paused, string? failure)
    {
        var expected = new SubscriptionInfo(group, new("root")
        {
            Policy = policy
        },
            generation, generation, checkpoint, issued, tail, paused, failure);
        await Assert.That(JsonSerializer.Serialize(db.GetSubscription("root", group), JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(expected, JsonDefaults.Options));
    }

    internal static async Task ResumeRefusalAsync(DatabaseEngine db, ZoneTreeStore store,
        SubscriptionRef group, DateTimeOffset time,
        List<(Command<DatabaseCommand> Operation, Result<object> Result, byte[] Saved)> receipts)
    {
        var request = new SetSubscriptionPausedRequest(Guid.NewGuid(), group, 1, false);
        var operation = db.CreateCommand(OperationKind.SetSubscriptionPaused, request.CommandId, "root", time, JsonSerializer.Serialize(request, JsonDefaults.Options));
        var before = SubscriptionProcessingAtomicAssertions.DomainImage(store, group, operation.CommandId);
        var position = store.Position;
        var failure = db.Apply(operation);
        await Assert.That(Errors.Code(failure.Problem).Value).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(failure.Problem?.Detail).IsEqualTo("A parked subscription requires an explicit seek or new group before resuming.");

        await Assert.That(failure.Value).IsNull();
        await SubscriptionProcessingAtomicAssertions.OwnOutcomeOnlyAsync(store, group, operation, before, position + 1);
        var saved = SubscriptionProcessingAtomicAssertions.OutcomeBytes(store, group, operation.CommandId);
        var cut = QueueWholeFlowStorage.Bytes(store);
        position = store.Position;
        var identity = OrleansSerialization.Serialize(store.Identity);
        var replay = db.Apply(operation);
        await Assert.That(JsonSerializer.Serialize(replay, JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(failure, JsonDefaults.Options));
        await Assert.That(SubscriptionProcessingAtomicAssertions.OutcomeBytes(store, group, operation.CommandId)
            .AsSpan().SequenceEqual(saved)).IsTrue();
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(store, cut, position, identity);
        receipts.Add((operation, failure, saved));
    }

    internal static async Task ReplayAsync(DatabaseEngine db, ZoneTreeStore store, SubscriptionRef group,
        Command<DatabaseCommand> operation, Result<object> original, byte[] saved)
    {
        var cut = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var identity = OrleansSerialization.Serialize(store.Identity);
        var replay = db.Apply(operation);
        if (Errors.Code(original.Problem).Value is not null)
        {
            await Assert.That(Errors.Code(replay.Problem).Value).IsEqualTo(Errors.Code(original.Problem).Value);
            await Assert.That(replay.Problem?.Detail).IsEqualTo(original.Problem?.Detail);

            await Assert.That(replay.Value).IsNull();
        }
        else if (original.Value is CommitReceipt)
        {
            await ReplayResultAssertions.Same<CommitReceipt>(replay, original);
        }
        else if (original.Value is SubscriptionInfo)
        {
            await ReplayResultAssertions.Same<SubscriptionInfo>(replay, original);
        }
        else
        {
            await ReplayResultAssertions.Same<ReceiveSubscriptionResult>(replay, original);
        }
        await Assert.That(SubscriptionProcessingAtomicAssertions.OutcomeBytes(store, group, operation.CommandId)
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
        var operation = db.CreateCommand(OperationKind.SubscriptionDelivery, command.CommandId, "root", time, JsonSerializer.Serialize(command, JsonDefaults.Options));
        var before = SubscriptionProcessingAtomicAssertions.DomainImage(store, group, command.CommandId);
        var position = store.Position;
        var failure = db.Apply(operation);
        await Assert.That(Errors.Code(failure.Problem).Value).IsEqualTo(ErrorCode.TokenInvalidated);
        await Assert.That(failure.Problem?.Detail).IsEqualTo("The subscription token scope or generation is invalid.");

        await Assert.That(failure.Value).IsNull();
        await SubscriptionProcessingAtomicAssertions.OwnOutcomeOnlyAsync(store, group, operation, before, position + 1);
    }

    internal static async Task OldReceiveAsync(DatabaseEngine db, ZoneTreeStore store, SubscriptionRef group,
        Command<DatabaseCommand> operation, Result<object> original, byte[] saved)
    {
        var cut = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var identity = OrleansSerialization.Serialize(store.Identity);
        var result = db.Apply(operation);
        await Assert.That(Errors.Code(result.Problem).Value).IsEqualTo(ErrorCode.TokenInvalidated);
        await Assert.That(result.Problem?.Detail).IsEqualTo("The cached receive belongs to an earlier subscription generation.");

        await Assert.That(result.Value).IsNull();
        var retained = SubscriptionProcessingAtomicAssertions.OutcomeBytes(store, group, operation.CommandId);
        await Assert.That(retained.AsSpan().SequenceEqual(saved)).IsTrue();
        await ReplayResultAssertions.Same<ReceiveSubscriptionResult>(
            OrleansSerialization.Deserialize<StoredOutcome>(retained).Result, original);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(store, cut, position, identity);
    }
}

