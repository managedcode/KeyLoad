using System.Text.Json;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ResourceExecution;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class SubscriptionProcessingAtomicAssertions
{
    internal static string[] DomainImage(ZoneTreeStore store, SubscriptionRef group, Guid id)
    {
        var own = new[] { KeySpace.PartitionOutcome(group.Source.Partition, "root", id),
            KeySpace.OutcomeLocator(group.Source.Partition, "root", id), KeySpace.ClockBytes, KeySpace.AppliedBytes }
            .Select(key => Convert.ToHexString(key) + ":").ToArray();
        return QueueWholeFlowStorage.Bytes(store).Where(row =>
            !own.Any(key => row.StartsWith(key, StringComparison.Ordinal))).ToArray();
    }

    internal static byte[] OutcomeBytes(ZoneTreeStore store, SubscriptionRef group, Guid id)
        => store.Read(view => view.ReadOwnedValue(KeySpace.PartitionOutcome(group.Source.Partition, "root", id)))
            ?? throw new InvalidOperationException("The original processing outcome is absent.");

    internal static async Task RollbackAsync(DatabaseEngine db, ZoneTreeStore store, SubscriptionRef group,
        Command<DatabaseCommand> operation, Result<object> failure, string[] before, long position)
    {
        await FailureAsync(failure);
        await OwnOutcomeOnlyAsync(store, group, operation, before, position);
        await Assert.That(db.GetDocument("root", new(group.Source.Partition, "orders", "first"))).IsNull();
        await Assert.That(db.GetDocument("root", new(group.Source.Partition, "orders", "second"))).IsNull();
        await SubscriptionCheckpointAuthorityAssertions.StatusAsync(db, group, 0, 3, 3);
        await FailureAsync(OrleansSerialization.Deserialize<StoredOutcome>(OutcomeBytes(store, group, operation.CommandId)).Result);
    }

    internal static async Task OwnOutcomeOnlyAsync(ZoneTreeStore store, SubscriptionRef group,
        Command<DatabaseCommand> operation, string[] before, long position)
    {
        await Assert.That(DomainImage(store, group, operation.CommandId)).IsEquivalentTo(before, CollectionOrdering.Matching);
        await Assert.That(store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Clock(store)).IsEqualTo(new DateTimeOffset(operation.Timestamp));
        var key = KeySpace.PartitionOutcome(group.Source.Partition, "root", operation.CommandId);
        var locator = store.Read(view => view.ReadOwnedValue(KeySpace.OutcomeLocator(
            group.Source.Partition, "root", operation.CommandId)));
        await Assert.That(locator is not null && locator.AsSpan().SequenceEqual(key)).IsTrue();
    }

    internal static async Task FailureReplayAsync(DatabaseEngine db, ZoneTreeStore store, SubscriptionRef group,
        Command<DatabaseCommand> operation, Result<object> original, byte[] saved)
    {
        var cut = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var identity = OrleansSerialization.Serialize(store.Identity);
        await FailureAsync(original);
        for (var repeat = 0; repeat < 2; repeat++)
        {
            var replay = db.Apply(operation);
            await FailureAsync(replay);
            await Assert.That(JsonSerializer.Serialize(replay, JsonDefaults.Options))
                .IsEqualTo(JsonSerializer.Serialize(original, JsonDefaults.Options));
            await Assert.That(OutcomeBytes(store, group, operation.CommandId).AsSpan().SequenceEqual(saved)).IsTrue();
            await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(store, cut, position, identity);
        }
    }

    private static async Task FailureAsync(Result<object> result)
    {
        await Assert.That(Errors.Code(result.Problem)).IsEqualTo(ErrorCode.RevisionConflict);
        await Assert.That(result.Problem?.Detail).IsEqualTo("The expected revision does not match.");

        await Assert.That(result.Value).IsNull();
    }

    internal static async Task SuccessAsync(DatabaseEngine db, SubscriptionRef group,
        SubscriptionProcessingRequest request, Result<object> result, TestDatabase originalOwner)
    {
        await Assert.That(Errors.Code(result.Problem)).IsNull();
        var processing = result.Get<SubscriptionProcessingResult>();
        await Assert.That(processing.AlreadyProcessed).IsFalse();
        await Assert.That(processing.OriginalEffectsToken).IsEqualTo(processing.Receipt.Token);
        await Assert.That(processing.Receipt.CommandId).IsEqualTo(request.CommandId);
        await Assert.That(processing.Receipt.Mutations).IsEquivalentTo(new MutationReceipt[]
        { new("putDocument", "orders", "first", 1), new("putDocument", "orders", "second", 1),
          new("Ack", "topic", "1", 1) }, CollectionOrdering.Matching);
        await DocumentsAsync(db, group, originalOwner);
        await SubscriptionCheckpointAuthorityAssertions.StatusAsync(db, group, 1, 3, 3);
    }

    internal static async Task DocumentsAsync(DatabaseEngine db, SubscriptionRef group, TestDatabase originalOwner)
    {
        foreach (var item in new[] { (Id: "first", Json: "{\"n\":1}"), (Id: "second", Json: "{\"n\":2}") })
        {
            var reference = originalOwner.ExpectedReference(new(group.Source.Partition, "orders", item.Id));
            var expected = new DocumentResult(reference, 1, item.Json, false, []);
            await Assert.That(JsonSerializer.Serialize(db.GetDocument("root", reference), JsonDefaults.Options))
                .IsEqualTo(JsonSerializer.Serialize(expected, JsonDefaults.Options));
        }
    }

    internal static async Task SuccessReplayAsync(DatabaseEngine db, ZoneTreeStore store, SubscriptionRef group,
        Command<DatabaseCommand> operation, Result<object> original, byte[] saved)
    {
        var cut = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var identity = OrleansSerialization.Serialize(store.Identity);
        await ReplayResultAssertions.Same<SubscriptionProcessingResult>(db.Apply(operation), original);
        await ReplayResultAssertions.Same<SubscriptionProcessingResult>(
            OrleansSerialization.Deserialize<StoredOutcome>(OutcomeBytes(store, group, operation.CommandId)).Result, original);
        await Assert.That(OutcomeBytes(store, group, operation.CommandId).AsSpan().SequenceEqual(saved)).IsTrue();
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(store, cut, position, identity);
    }
}

