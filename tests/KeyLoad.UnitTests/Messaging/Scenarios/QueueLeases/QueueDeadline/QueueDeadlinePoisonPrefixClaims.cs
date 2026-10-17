using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueDeadlinePoisonPrefixClaims
{
    internal static CommitToken NextToken(DatabaseEngine database, ZoneTreeStore store, QueueLaneRef lane)
    {
        var placement = database.ReadAtomicPartitionPlacement(QueueDeadlinePoisonPrefixProtocol.Root, new(QueueDeadlinePoisonPrefixProtocol.First, lane.Partition));
        return new(placement.Incarnation, lane.Partition.AtomicPartitionId, checked(store.Position + QueueDeadlinePoisonPrefixProtocol.First), placement.PlacementEpoch);
    }

    internal static ReplicatedOperation Operation<T>(DatabaseEngine database, OperationKind kind, Guid id,
        T payload, string subject, DateTimeOffset time)
        => database.PrepareQueueRetryOperation(database.NormalizeOperation(new(id, kind, subject, time,
            System.Text.Json.JsonSerializer.Serialize(payload, JsonDefaults.Options))));

    internal static async Task<ReceiveResult> ReceiveAsync(DatabaseEngine database, ZoneTreeStore store, QueueLaneRef lane,
        MessageMetadata[] ready, string subject, DateTimeOffset time, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var request = new ReceiveRequest(Guid.NewGuid(), lane, MaxMessages: ready.Length);
        var expectedToken = NextToken(database, store, lane);
        var expected = ready.OrderBy(value => value.ReadySequence).Select(metadata =>
        {
            var claims = new DeliveryClaims(lane, metadata.Id, subject, metadata.LeaseVersion + QueueDeadlinePoisonPrefixProtocol.First,
                metadata.DeliveryGeneration, expectedToken.Incarnation);
            return new Delivery(metadata.Id, QueueDeadlinePoisonPrefixProtocol.Json, QueueDeadlinePoisonPrefixProtocol.Json, database.Sign(claims), metadata.LeaseVersion + QueueDeadlinePoisonPrefixProtocol.First,
                time.AddSeconds(QueueDeadlinePoisonPrefixProtocol.LeaseSeconds), metadata.Attempts + QueueDeadlinePoisonPrefixProtocol.First, metadata.DeliveryGeneration);
        }).ToImmutableArray();
        var operation = Operation(database, OperationKind.Receive, request.RequestId, request, subject, time);
        var result = database.Apply(operation);
        await NativeReplayResultAssertions.Same<ReceiveResult>(result,
            new OperationResult(null) { NativeValue = new ReceiveResult(request.RequestId, expected, expectedToken) });
        foreach (var delivery in result.Get<ReceiveResult>().Deliveries)
        {
            var source = ready.Single(value => value.Id == delivery.Id);
            await Assert.That(database.Verify<DeliveryClaims>(delivery.Token)).IsEqualTo(new DeliveryClaims(lane,
                source.Id, subject, source.LeaseVersion + QueueDeadlinePoisonPrefixProtocol.First, source.DeliveryGeneration, expectedToken.Incarnation));
        }
        var bytes = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var identity = NativeSerialization.Serialize(store.Identity);
        await NativeReplayResultAssertions.Same<ReceiveResult>(database.Apply(operation), result);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(store, bytes, position, identity);
        return result.Get<ReceiveResult>();
    }

    internal static async Task CurrentClaimAsync(DatabaseEngine database, ZoneTreeStore store, QueueLaneRef lane,
        ReceiveResult original, string subject, DateTimeOffset time, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var bytes = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var identity = NativeSerialization.Serialize(store.Identity);
        var request = new ReceiveRequest(original.RequestId, lane, MaxMessages: original.Deliveries.Length);
        var operation = Operation(database, OperationKind.Receive, request.RequestId, request, subject, time);
        await NativeReplayResultAssertions.Same<ReceiveResult>(database.Apply(operation),
            new(null)
            {
                NativeValue = original
            });
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(store, bytes, position, identity);
    }
}
