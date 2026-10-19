using KeyLoad.Core;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueClaimVersionExhaustionAssertions
{
    internal static async Task FailureAsync(Result<object> result)
    {
        await Assert.That(Errors.Code(result.Problem)).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(result.Problem?.Detail).IsEqualTo(QueueClaimVersionExhaustionProtocol.Detail);

        await Assert.That(result.Value).IsNull();
    }

    internal static async Task ReadyAsync(DatabaseEngine database, ZoneTreeStore store, QueueClaimVersionExhaustionState state)
    {
        await Assert.That(database.InspectMessage(QueueClaimVersionExhaustionProtocol.Root, state.Lane, state.Body.Id))
            .IsEqualTo(new MessageInspection(state.DamagedMetadata, state.Body.PayloadJson, state.Body.HeadersJson));
        var bytes = OrleansSerialization.Serialize(state.Body).LongLength;
        await Assert.That(store.Read(view => view.GetRecord<MessageBody>(KeySpace.Partition(QueueClaimVersionExhaustionProtocol.BodySpace,
            state.Lane.Partition, state.Lane.Queue, state.Body.Id)))).IsEqualTo(state.Body);
        await Assert.That(store.Read(view => view.GetRecord<QueueCounters>(KeySpace.Partition(QueueClaimVersionExhaustionProtocol.CounterSpace,
            state.Lane.Partition, state.Lane.Queue)))).IsEqualTo(new QueueCounters(1, bytes, 0, 0, 1));
        await Assert.That(store.Read(view => view.GetRecord<string>(KeySpace.Partition(QueueClaimVersionExhaustionProtocol.ReadySpace,
            state.Lane.Partition, state.Lane.Queue, 1L, state.Body.Id)))).IsEqualTo(state.Body.Id);
        await Assert.That(store.Read(view => view.Scan(KeySpace.Partition(QueueClaimVersionExhaustionProtocol.LeaseSpace,
            state.Lane.Partition, state.Lane.Queue), QueueClaimVersionExhaustionProtocol.ImageRecords).Records)).IsEmpty();
    }

    internal static async Task ReplayAsync(DatabaseEngine database, ZoneTreeStore store,
        QueueClaimVersionExhaustionState state, Result<object> original)
    {
        var key = KeySpace.PartitionOutcome(state.Lane.Partition, QueueClaimVersionExhaustionProtocol.Root, state.Operation.CommandId);
        var retained = store.Read(view => view.ReadOwnedValue(key))!;
        var stored = OrleansSerialization.Deserialize<StoredOutcome>(retained);
        await EqualAsync(stored.Result, original);
        var bytes = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        await EqualAsync(database.Apply(state.Operation), original);
        await EqualAsync(database.ResolveOutcome(state.Operation), original);
        await Assert.That(store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(store)).IsEquivalentTo(bytes, CollectionOrdering.Matching);
        await Assert.That(store.Read(view => view.ReadOwnedValue(key))).IsEquivalentTo(retained, CollectionOrdering.Matching);
    }

    internal static async Task EqualAsync<T>(T actual, T expected)
        => await Assert.That(OrleansSerialization.Serialize(actual).AsSpan().SequenceEqual(OrleansSerialization.Serialize(expected))).IsTrue();

    internal static async Task DomainAsync(ZoneTreeStore store, QueueLaneRef lane, string[] expected)
        => await Assert.That(Domain(store, lane)).IsEquivalentTo(expected, CollectionOrdering.Matching);

    internal static string[] Domain(ZoneTreeStore store, QueueLaneRef lane) => store.Read(view =>
        QueueClaimVersionExhaustionProtocol.Families.SelectMany(family =>
        {
            var page = view.Scan(KeySpace.Partition(family, lane.Partition, lane.Queue), QueueClaimVersionExhaustionProtocol.ImageRecords);
            if (page.HasMore)
            {
                throw new InvalidOperationException("The version-exhaustion domain exceeded its original fixture bound.");
            }
            return page.Records.Select(row => Convert.ToHexString(row.Key.Span) + ":" + Convert.ToHexString(row.Value.Span));
        }).ToArray());
}
