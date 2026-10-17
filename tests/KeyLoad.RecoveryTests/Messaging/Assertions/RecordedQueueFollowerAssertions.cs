using KeyLoad.Core;
using KeyLoad.Storage;
using static KeyLoad.RecoveryTests.Features.Messaging.RecordedQueueFollowerProtocol;

namespace KeyLoad.RecoveryTests.Features.Messaging;

internal static class RecordedQueueFollowerAssertions
{
    internal static async Task ReceiptAsync(RecordedQueueFollowerOutcome outcome, string kind, long version, string message = Message)
    {
        await Assert.That(outcome.Result.Error).IsNull();
        await EqualAsync(outcome.Result.Get<CommitReceipt>(), new CommitReceipt(outcome.Entry.Operation!.Id,
            outcome.ExpectedToken, [new(kind, Queue, message, version)], DurabilityProfile.ProcessDurable));
    }

    internal static async Task<Delivery> ClaimAsync(PeerDiscoveryNativeCluster cluster, RecordedQueueFollowerOutcome outcome,
        string message = Message)
    {
        await Assert.That(outcome.Result.Error).IsNull();
        var claims = new DeliveryClaims(Lane, message, PeerDiscoveryNativeClockProfile.PrincipalId,
            InitialLeaseVersion, InitialDeliveryGeneration, outcome.ExpectedToken.Incarnation);
        var signed = cluster.Sender.Database.Sign(claims);
        var expected = new Delivery(message, Payload, Headers, signed, InitialLeaseVersion,
            outcome.Entry.Operation!.EvaluatedAt.AddSeconds(LeaseSeconds), FirstAttempt, InitialDeliveryGeneration);
        await EqualAsync(outcome.Result.Get<ReceiveResult>(), new ReceiveResult(outcome.Entry.Operation.Id, [expected], outcome.ExpectedToken));
        foreach (var node in cluster.Nodes)
        {
            await EqualAsync(node.Database.Verify<DeliveryClaims>(signed), claims);
        }
        return expected;
    }

    internal static async Task StateAsync(PeerDiscoveryNativeCluster cluster, MessageMetadata metadata,
        MessageBody? body, QueueCounters counters, string? family = null, object? deadline = null)
    {
        foreach (var node in cluster.Nodes)
        {
            var store = node.Database.Store;
            var observed = node.Database.InspectMessage(PeerDiscoveryNativeClockProfile.PrincipalId, Lane, metadata.Id);
            await Assert.That(observed).IsNotNull();
            await EqualAsync(observed!.Metadata, metadata);
            await Assert.That(observed.PayloadJson).IsEqualTo(body?.PayloadJson);
            await Assert.That(observed.HeadersJson).IsEqualTo(body?.HeadersJson);
            await EqualAsync(store.Read(view => view.GetRecord<MessageBody>(KeySpace.Partition(BodyFamily, Partition, Queue, metadata.Id))), body);
            await EqualAsync(store.Read(view => view.GetRecord<QueueCounters>(KeySpace.Partition(CounterFamily, Partition, Queue))), counters);
            foreach (var candidate in new[] { ReadyFamily, ScheduledFamily, LeasedFamily })
            {
                var prefix = KeySpace.Partition(candidate, Partition, Queue);
                var page = store.Read(view => view.Scan(prefix, ImageRecords));
                await Assert.That(page.HasMore).IsFalse();
                var key = candidate != family ? []
                    : KeySpace.Partition(candidate, Partition, Queue, deadline!, metadata.Id);
                string[] expected = candidate == family
                    ? [Convert.ToHexString(key) + ":" + Convert.ToHexString(NativeSerialization.Serialize(metadata.Id))] : [];
                await Assert.That(page.Records.Select(row => Convert.ToHexString(row.Key.Span) + ":"
                    + Convert.ToHexString(row.Value.Span))).IsEquivalentTo(expected, TUnit.Assertions.Enums.CollectionOrdering.Matching);
            }
        }
    }

    internal static async Task EqualAsync<T>(T actual, T expected)
    {
        if (actual is null || expected is null)
        {
            await Assert.That(actual is null).IsTrue();
            await Assert.That(expected is null).IsTrue();
            return;
        }
        await Assert.That(NativeSerialization.Serialize(actual).AsSpan().SequenceEqual(NativeSerialization.Serialize(expected))).IsTrue();
    }
}
