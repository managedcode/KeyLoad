using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Messaging;

internal static class QueueScheduledAuthorityImage
{
    internal static Task ScheduledAsync(DatabaseEngine owner, ZoneTreeStore store, QueueScheduledAuthorityState state)
        => RequireAsync(owner, store, state, new MessageMetadata(QueueLifecycleTestProtocol.Pending,
            MessageState.Scheduled, 0, 1, 0, state.Due, null), leased: false, terminal: false, healthy: false);

    internal static async Task LeasedAsync(DatabaseEngine owner, ZoneTreeStore store, QueueScheduledAuthorityState state, Delivery delivery)
    {
        await Assert.That(delivery.Id).IsEqualTo(QueueLifecycleTestProtocol.Pending);
        await RequireAsync(owner, store, state, new MessageMetadata(QueueLifecycleTestProtocol.Pending,
            MessageState.Leased, 1, 3, 1, null, null, QueueLifecycleTestProtocol.Administrator, 1,
            state.Flow.Time.AddSeconds(QueueLifecycleTestProtocol.LeaseSeconds)), leased: true, terminal: false, healthy: false);
    }

    internal static Task TerminalAsync(DatabaseEngine owner, ZoneTreeStore store, QueueScheduledAuthorityState state, bool healthy)
        => RequireAsync(owner, store, state, new MessageMetadata(QueueLifecycleTestProtocol.Pending,
            MessageState.Acked, 1, 4, 1, null, null, LeaseVersion: 1), leased: false, terminal: true, healthy);

    private static async Task RequireAsync(DatabaseEngine owner, ZoneTreeStore store, QueueScheduledAuthorityState state,
        MessageMetadata metadata, bool leased, bool terminal, bool healthy)
    {
        var flow = state.Flow;
        var literal = new EnqueueMessage(flow.Lane.Queue, QueueLifecycleTestProtocol.Pending,
            QueueLifecycleTestProtocol.Payload, QueueLifecycleTestProtocol.Headers, NotBefore: state.Due);
        var body = new MessageBody(literal.MessageId, literal.PayloadJson, literal.HeadersJson, literal.OrderingKey, JsonData.Fingerprint(literal));
        var bytes = OrleansSerialization.Serialize(body).LongLength;
        var counters = new QueueCounters(terminal ? 0 : 1, terminal ? 0 : bytes,
            leased ? 1 : 0, leased ? bytes : 0, healthy ? 2 : metadata.ReadySequence);
        List<string> expected = [];
        if (!terminal)
        {
            expected.Add(leased ? Row(state, "lease", body.Id, metadata.LeaseUntil!.Value, body.Id)
                : Row(state, "scheduled", body.Id, state.Due, body.Id));
            expected.Add(Row(state, "message-body", body, body.Id));
        }
        List<string> metadataRows = [Row(state, "message-meta", metadata, metadata.Id)];
        if (healthy)
        {
            metadataRows.Add(Row(state, "message-meta", new MessageMetadata(QueuePendingAuthorityProtocol.Healthy,
                MessageState.Acked, 1, 3, 2, null, null, LeaseVersion: 1), QueuePendingAuthorityProtocol.Healthy));
        }
        expected.AddRange(metadataRows.Order(StringComparer.Ordinal));
        expected.Add(Row(state, "queue-counters", counters));
        await Assert.That(QueueLifecycleImage.Capture(store, flow.Lane)).IsEquivalentTo(expected, CollectionOrdering.Matching);
        var order = store.Read(view => view.Scan(KeySpace.Partition("queue-order", flow.Partition, flow.Lane.Queue), 4));
        await Assert.That(order.HasMore).IsFalse();
        await Assert.That(order.Records).IsEmpty();
        var inspection = owner.InspectMessage(QueueLifecycleTestProtocol.Administrator, flow.Lane, metadata.Id)!;
        await Assert.That(inspection.Metadata).IsEqualTo(metadata);
        await Assert.That(inspection.PayloadJson).IsEqualTo(terminal ? null : literal.PayloadJson);
        await Assert.That(inspection.HeadersJson).IsEqualTo(terminal ? null : literal.HeadersJson);
        await EffectsAsync(owner, store, state);
    }

    private static async Task EffectsAsync(DatabaseEngine owner, ZoneTreeStore store, QueueScheduledAuthorityState state)
    {
        List<string> expected = [];
        foreach (var (request, receipt) in state.Processed)
        {
            var effect = (PutDocument)request.Effects.Single();
            var reference = new EntityRef(state.Flow.Partition, effect.Collection, effect.Id)
            {
                Lifetime = state.DocumentLifetime
            };
            var document = owner.GetDocument(QueueLifecycleTestProtocol.Administrator, reference)!;
            await Assert.That(document.Reference).IsEqualTo(reference);
            await Assert.That(document.Revision).IsEqualTo(1);
            await Assert.That(document.Json).IsEqualTo(QueueLifecycleTestProtocol.Payload);
            await Assert.That(document.Redacted).IsFalse();
            await Assert.That(document.RedactedFields).IsEmpty();
            var fingerprint = JsonData.Fingerprint(new
            {
                request.Lane,
                MessageId = effect.Id,
                request.HandlerScope,
                request.ExecutionGeneration,
                request.Effects,
                Id = QueueLifecycleTestProtocol.Administrator
            });
            expected.Add(Row(state, "inbox", new InboxRecord(fingerprint, receipt), effect.Id, request.HandlerScope, request.ExecutionGeneration));
        }
        var page = store.Read(view => view.Scan(KeySpace.Partition("inbox", state.Flow.Partition, state.Flow.Lane.Queue), 4));
        await Assert.That(page.HasMore).IsFalse();
        await Assert.That(page.Records.Select(row => Convert.ToHexString(row.Key.Span) + ":" + Convert.ToHexString(row.Value.Span)))
            .IsEquivalentTo(expected.Order(StringComparer.Ordinal), CollectionOrdering.Matching);
        if (state.Processed.Count == 0)
        {
            await Assert.That(owner.GetDocument(QueueLifecycleTestProtocol.Administrator,
                new(state.Flow.Partition, QueueLifecycleTestProtocol.Collection, QueueLifecycleTestProtocol.Pending))).IsNull();
        }
    }

    private static string Row<T>(QueueScheduledAuthorityState state, string space, T value, params object[] rest)
        => Convert.ToHexString(KeySpace.Partition(space, state.Flow.Partition,
            new object[] { state.Flow.Lane.Queue }.Concat(rest).ToArray())) + ":" + Convert.ToHexString(OrleansSerialization.Serialize(value));
}
