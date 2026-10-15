using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ResourceExecution;
using TUnit.Assertions.Enums;
using static KeyLoad.UnitTests.Features.Messaging.SubscriptionContinuationProtocol;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class SubscriptionContinuationAssertions
{
    internal static async Task ClaimAsync(DatabaseEngine db, ZoneTreeStore store, SubscriptionRef group,
        ReceiveSubscriptionRequest request, OperationResult actual, SourceEventRecord source,
        int attempt, long version, DateTimeOffset time, long checkpoint, long issued, long tail, CommitToken token)
    {
        var claims = new GroupClaims(Purpose, store.Identity.Incarnation, group, Generation, OwnershipEpoch,
            source.Position, version, Subject, PolicyEpoch, PolicyEpoch);
        var signed = db.Sign(claims);
        var delivery = new SubscriptionDelivery(source, signed, version, time.AddSeconds(LeaseSeconds), attempt);
        var status = new SubscriptionInfo(group, new(Subject)
        {
            Policy = new()
            {
                MaxWindow = Window
            }
        },
            Generation, OwnershipEpoch, checkpoint, issued, tail, false, null);
        var expected = new ReceiveSubscriptionResult(request.RequestId, [delivery], status, token);
        await EqualAsync(actual.Get<ReceiveSubscriptionResult>(), expected);
        await EqualAsync(db.Verify<GroupClaims>(actual.Get<ReceiveSubscriptionResult>().Deliveries.Single().Token), claims);
        await Assert.That(actual.Error).IsNull();
        var row = store.Read(view => view.ReadOwnedValue(
            SubscriptionCheckpointAuthorityFlow.WindowKey(group, source.Position)));
        await Assert.That(row).IsNotNull();
        await EqualAsync(NativeSerialization.Deserialize<GroupDelivery>(row!),
            new(source.Position, GroupDeliveryState.Leased, attempt, version,
                LeaseUntil: time.AddSeconds(LeaseSeconds), PrincipalId: Subject));
        await SubscriptionCheckpointAuthorityAssertions.StatusAsync(db, group, checkpoint, issued, tail);
    }

    internal static async Task ReceiptAsync(OperationResult actual, Guid id, CommitToken token, MutationReceipt mutation)
    {
        await Assert.That(actual.Error).IsNull();
        await EqualAsync(actual.Get<CommitReceipt>(), new CommitReceipt(id, token, [mutation], DurabilityProfile.ProcessDurable));
    }

    internal static async Task ReplayAsync(DatabaseEngine db, ZoneTreeStore store,
        SubscriptionContinuationOutcome entry, DateTimeOffset time, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var image = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var identity = NativeSerialization.Serialize(store.Identity);
        var actual = QueueWholeFlowStorage.Apply(db, entry.Kind, entry.Request, entry.Id, time);
        if (entry.Result.Error is not null)
        {
            await Assert.That(actual).IsEqualTo(entry.Result);
            await Assert.That(actual.Json).IsNull();
            await Assert.That(actual.NativeValue).IsNull();
        }
        else if (entry.Result.NativeValue is CommitReceipt)
        {
            await NativeReplayResultAssertions.Same<CommitReceipt>(actual, entry.Result);
        }
        else
        {
            await NativeReplayResultAssertions.Same<ReceiveSubscriptionResult>(actual, entry.Result);
        }
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(store, image, position, identity);
    }

    internal static async Task TerminalAsync(DatabaseEngine db, ZoneTreeStore store, SubscriptionRef group,
        ReceiveSubscriptionResult original, DateTimeOffset healthyTime)
    {
        await SubscriptionCheckpointAuthorityAssertions.StatusAsync(db, group, HealthyPosition, HealthyPosition, HealthyPosition);
        await SubscriptionCheckpointAuthorityAssertions.NoWindowAsync(store, group);
        var expected = original.Deliveries.Select(delivery => delivery.Event).Append(
            new SourceEventRecord(group.Source, HealthyPosition, HealthyPosition,
                new(HealthyId, EventType, HealthyPayload), healthyTime)).ToArray();
        var actual = db.ReadEventSource(Subject, new(group.Source));
        await Assert.That(actual.Events).IsEquivalentTo(expected, CollectionOrdering.Matching);
        await EqualAsync(actual.Head, new EventSourceHead(HealthyPosition, RetainedFloor, group.Source.Generation));
        await Assert.That(actual.Source).IsEqualTo(group.Source);
        await Assert.That(actual.HasMore).IsFalse();
        await SourceRowsAsync(store, group.Source, expected);
    }

    private static async Task SourceRowsAsync(ZoneTreeStore store, EventSourceRef source, SourceEventRecord[] expected)
    {
        var prefix = KeySpace.Partition(SourceFamily, source.Partition, source.Resource, source.Generation);
        var page = store.Read(view => view.Scan(prefix, Window + SingleEventCount + ScanLookahead));
        await Assert.That(page.HasMore).IsFalse();
        await Assert.That(page.Records.Select(row => NativeSerialization.Deserialize<SourceEventRecord>(row.Value.Span)))
            .IsEquivalentTo(expected, CollectionOrdering.Matching);
        foreach (var record in expected)
        {
            var key = KeySpace.Partition(SourceFamily, source.Partition, source.Resource, source.Generation, record.Position);
            var bytes = store.Read(view => view.ReadOwnedValue(key));
            await Assert.That(bytes).IsNotNull();
            await EqualAsync(NativeSerialization.Deserialize<SourceEventRecord>(bytes!), record);
            var id = KeySpace.Partition(SourceIdentityFamily, source.Partition, source.Resource, source.Generation, record.Data.EventId);
            var identity = store.Read(view => view.ReadOwnedValue(id));
            await Assert.That(identity).IsNotNull();
            await EqualAsync(NativeSerialization.Deserialize<long>(identity!), record.Position);
        }
        var headKey = KeySpace.Partition(SourceHeadFamily, source.Partition, source.Resource);
        var head = store.Read(view => view.ReadOwnedValue(headKey));
        await Assert.That(head).IsNotNull();
        var storedBytes = page.Records.Sum(row => (long)row.Value.Length);
        await EqualAsync(NativeSerialization.Deserialize<TopicHead>(head!),
            new(HealthyPosition, RetainedFloor, source.Generation, storedBytes));
        var sequence = store.Read(view => view.ReadOwnedValue(KeySpace.Partition(SourceSequenceFamily, source.Partition)));
        await Assert.That(sequence).IsNotNull();
        await EqualAsync(NativeSerialization.Deserialize<long>(sequence!), HealthyPosition);
    }

    internal static async Task WindowAsync(ZoneTreeStore store, SubscriptionRef group, long checkpoint,
        long issued, IReadOnlyCollection<SubscriptionDelivery> deliveries)
    {
        var source = group.Source;
        var key = KeySpace.Partition(GroupFamily, source.Partition, source.Resource,
            source.Kind.ToString(), source.StreamId, source.Generation, group.GroupId);
        var state = store.Read(view => view.ReadOwnedValue(key));
        await Assert.That(state).IsNotNull();
        await EqualAsync(NativeSerialization.Deserialize<GroupState>(state!),
            new(new(Subject)
            {
                Policy = new()
                {
                    MaxWindow = Window
                }
            }, Generation, OwnershipEpoch, checkpoint, issued));
        var page = store.Read(view => view.Scan(SubscriptionCheckpointAuthorityFlow.WindowKey(group, null),
            Window + ScanLookahead));
        await Assert.That(page.HasMore).IsFalse();
        var expected = deliveries.Select(delivery => new GroupDelivery(delivery.Event.Position,
            GroupDeliveryState.Leased, delivery.Attempt, delivery.LeaseVersion,
            LeaseUntil: delivery.LeaseUntil, PrincipalId: Subject)).ToArray();
        await Assert.That(page.Records.Select(row => NativeSerialization.Deserialize<GroupDelivery>(row.Value.Span)))
            .IsEquivalentTo(expected, CollectionOrdering.Matching);
        foreach (var row in page.Records)
        {
            var delivery = NativeSerialization.Deserialize<GroupDelivery>(row.Value.Span);
            await Assert.That(row.Key.Span.SequenceEqual(
                SubscriptionCheckpointAuthorityFlow.WindowKey(group, delivery.Position))).IsTrue();
        }
    }

    internal static async Task SettledAsync(DatabaseEngine db, ZoneTreeStore store,
        SubscriptionContinuationOutcome entry, DateTimeOffset time, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var request = (ReceiveSubscriptionRequest)entry.Request;
        var original = OutcomeStoreOracle.ReadPartition(store, request.Subscription.Source.Partition, Subject, entry.Id)
            ?? throw new InvalidOperationException(MissingClaim);
        await EqualAsync(original.Get<ReceiveSubscriptionResult>(), entry.Result.Get<ReceiveSubscriptionResult>());
        var image = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var identity = NativeSerialization.Serialize(store.Identity);
        var refused = QueueWholeFlowStorage.Apply(db, entry.Kind, entry.Request, entry.Id, time);
        await Assert.That(refused.Error).IsEqualTo(ErrorCode.StaleLease);
        await Assert.That(refused.SafeDetail).IsEqualTo(StaleDetail);
        await Assert.That(refused.Json).IsNull();
        await Assert.That(refused.NativeValue).IsNull();
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(store, image, position, identity);
    }

    private static async Task EqualAsync<T>(T actual, T expected)
    {
        await SubscriptionNativeValueAssertions.EqualAsync(actual, expected);
    }
}
