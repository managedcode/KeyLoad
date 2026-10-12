using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ResourceExecution;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class SubscriptionInboxConflictAssertions
{
    internal static byte[] InboxBytes(ZoneTreeStore store, SubscriptionRef group)
        => store.Read(view => view.ReadOwnedValue(KeySpace.Partition("subscription-inbox", group.Source.Partition,
            group.Source.Resource, group.Source.Kind.ToString(), group.Source.StreamId, group.Source.Generation,
            group.GroupId, 1L, "worker", 1L)))
            ?? throw new InvalidOperationException("The original successful handler inbox is absent.");

    internal static async Task InitialClaimsAsync(DatabaseEngine db, SubscriptionRef group,
        System.Collections.Immutable.ImmutableArray<SubscriptionDelivery> deliveries, DateTimeOffset producedAt)
    {
        await Assert.That(deliveries.Select(d => d.Event.Position)).IsEquivalentTo(
            new long[] { 1, 2, 3 }, CollectionOrdering.Matching);
        for (var index = 0; index < deliveries.Length; index++)
        {
            var position = (long)index + 1;
            var expected = new SourceEventRecord(group.Source, position, position,
                new EventData("event" + position, "Created", "{\"n\":" + position + "}"), producedAt);
            await Assert.That(JsonSerializer.Serialize(deliveries[index].Event, JsonDefaults.Options))
                .IsEqualTo(JsonSerializer.Serialize(expected, JsonDefaults.Options));
            await Assert.That(deliveries[index].LeaseVersion).IsEqualTo(1L);
            await Assert.That(deliveries[index].Attempt).IsEqualTo(1);
        }
        await SubscriptionCheckpointAuthorityAssertions.StatusAsync(db, group, 0, 3, 3);
    }

    internal static async Task ClaimsAsync(System.Collections.Immutable.ImmutableArray<SubscriptionDelivery> current,
        System.Collections.Immutable.ImmutableArray<SubscriptionDelivery> original, DateTimeOffset until)
    {
        await Assert.That(current.Length).IsEqualTo(3);
        for (var index = 0; index < current.Length; index++)
        {
            var expected = new SubscriptionDelivery(original[index].Event, current[index].Token, 1L, until, 1);
            await Assert.That(JsonSerializer.Serialize(current[index], JsonDefaults.Options))
                .IsEqualTo(JsonSerializer.Serialize(expected, JsonDefaults.Options));
            await Assert.That(current[index].Token).IsNotEqualTo(original[index].Token);
        }
    }

    internal static async Task RefusalAsync(DatabaseEngine db, ZoneTreeStore store, SubscriptionRef group,
        ReplicatedOperation operation, byte[] inbox, string[] source,
        List<(ReplicatedOperation Operation, OperationResult Result, byte[] Saved)> history)
    {
        var before = SubscriptionProcessingAtomicAssertions.DomainImage(store, group, operation.Id);
        var position = store.Position;
        var failure = db.Apply(operation);
        await FailureAsync(failure);
        await SubscriptionProcessingAtomicAssertions.OwnOutcomeOnlyAsync(store, group, operation, before, position + 1);
        await PreservedAsync(db, store, group, inbox, source);
        await SubscriptionParkedRecoveryAssertions.StatusAsync(db, group, new() { MaxWindow = 3 }, 2, 0, 3, 3, false, null);
        var saved = SubscriptionProcessingAtomicAssertions.OutcomeBytes(store, group, operation.Id);
        history.Add((operation, failure, saved));
        await ReplayAsync(db, store, group, history[^1]);
        await ReplayAsync(db, store, group, history[^1]);
    }

    internal static async Task RepairedAsync(DatabaseEngine db, ZoneTreeStore store, SubscriptionRef group,
        SubscriptionProcessingRequest request, OperationResult result, OperationResult original, byte[] inbox, string[] source)
    {
        await Assert.That(result.Error).IsNull();
        var processing = result.Get<SubscriptionProcessingResult>();
        await Assert.That(processing.AlreadyProcessed).IsTrue();
        await Assert.That(processing.OriginalEffectsToken).IsEqualTo(original.Get<SubscriptionProcessingResult>().OriginalEffectsToken);
        await Assert.That(processing.Receipt.CommandId).IsEqualTo(request.CommandId);
        await Assert.That(processing.Receipt.Mutations).IsEquivalentTo(
            new MutationReceipt[] { new("Ack", "topic", "1", 1) }, CollectionOrdering.Matching);
        await SubscriptionParkedRecoveryAssertions.StatusAsync(db, group, new() { MaxWindow = 3 }, 2, 1, 3, 3, false, null);
        await PreservedAsync(db, store, group, inbox, source);
    }

    internal static async Task PreservedAsync(DatabaseEngine db, ZoneTreeStore store,
        SubscriptionRef group, byte[] inbox, string[] source)
    {
        await PreservedDocumentsAndInboxAsync(db, store, group, inbox);
        await SubscriptionCheckpointAuthorityAssertions.SourceAsync(db, store, group.Source, source);
    }

    internal static async Task PreservedDocumentsAndInboxAsync(DatabaseEngine db, ZoneTreeStore store,
        SubscriptionRef group, byte[] inbox)
    {
        await SubscriptionProcessingAtomicAssertions.DocumentsAsync(db, group);
        await Assert.That(InboxBytes(store, group).AsSpan().SequenceEqual(inbox)).IsTrue();
    }

    internal static async Task ReplayAsync(DatabaseEngine db, ZoneTreeStore store, SubscriptionRef group,
        (ReplicatedOperation Operation, OperationResult Result, byte[] Saved) entry)
    {
        var cut = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var identity = NativeSerialization.Serialize(store.Identity);
        var replay = db.Apply(entry.Operation);
        if (entry.Result.Error is not null)
        {
            await FailureAsync(replay);
            await Assert.That(JsonSerializer.Serialize(replay, JsonDefaults.Options))
                .IsEqualTo(JsonSerializer.Serialize(entry.Result, JsonDefaults.Options));
        }
        else if (entry.Result.NativeValue is SubscriptionProcessingResult)
        { await NativeReplayResultAssertions.Same<SubscriptionProcessingResult>(replay, entry.Result); }
        else if (entry.Result.NativeValue is SubscriptionInfo)
        { await NativeReplayResultAssertions.Same<SubscriptionInfo>(replay, entry.Result); }
        else
        { await NativeReplayResultAssertions.Same<CommitReceipt>(replay, entry.Result); }
        var saved = SubscriptionProcessingAtomicAssertions.OutcomeBytes(store, group, entry.Operation.Id);
        await Assert.That(saved.AsSpan().SequenceEqual(entry.Saved)).IsTrue();
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(store, cut, position, identity);
    }

    internal static async Task FencedOriginalProcessingAsync(DatabaseEngine db, ZoneTreeStore store, SubscriptionRef group,
        (ReplicatedOperation Operation, OperationResult Result, byte[] Saved) original)
    {
        var cut = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var identity = NativeSerialization.Serialize(store.Identity);
        var replay = db.Apply(original.Operation);
        await Assert.That(replay.Error).IsEqualTo(ErrorCode.TokenInvalidated);
        await Assert.That(replay.SafeDetail).IsEqualTo("The subscription token scope or generation is invalid.");
        await Assert.That(replay.Json).IsNull();
        await Assert.That(replay.NativeValue).IsNull();
        var retained = SubscriptionProcessingAtomicAssertions.OutcomeBytes(store, group, original.Operation.Id);
        await Assert.That(retained.AsSpan().SequenceEqual(original.Saved)).IsTrue();
        await NativeReplayResultAssertions.Same<SubscriptionProcessingResult>(
            NativeSerialization.Deserialize<StoredOutcome>(retained).Result, original.Result);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(store, cut, position, identity);
    }

    private static async Task FailureAsync(OperationResult result)
    {
        await Assert.That(result.Error).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(result.SafeDetail).IsEqualTo("The subscription input was completed with different effects.");
        await Assert.That(result.Json).IsNull();
        await Assert.That(result.NativeValue).IsNull();
    }
}

