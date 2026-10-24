using KeyLoad.Core.Messaging;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Messaging;

internal static class QueueDeadLetterSelectedImage
{
    internal static async Task RequireAsync(DatabaseEngine database, ZoneTreeStore store,
        QueueDeadLetterSelectedState state, bool settled = false, bool healthy = false)
    {
        foreach (var (id, sequence, body) in new[]
        {
            (QueueDeadLetterSelectedProtocol.Original, QueueDeadLetterSelectedProtocol.One, state.FirstBody),
            (QueueDeadLetterSelectedProtocol.Foreign, QueueDeadLetterSelectedProtocol.Two, state.SecondBody)
        })
        {
            var terminal = settled && id == state.Target;
            var expected = QueueDeadLetterSelectedSetup.Parked(id, sequence);
            if (terminal)
            {
                expected = Terminal(expected, state.Kind);
            }
            await MessageAsync(database, store, state.Lane, expected, terminal ? null : body);
        }
        var retained = state.Target == QueueDeadLetterSelectedProtocol.Original ? state.SecondBody : state.FirstBody;
        var retainedBytes = OrleansSerialization.Serialize(retained).LongLength;
        var allBytes = OrleansSerialization.Serialize(state.FirstBody).LongLength + OrleansSerialization.Serialize(state.SecondBody).LongLength;
        var readySequence = healthy ? HealthySequence(state) : settled && state.Kind == QueueDeadLetterSelectedOperation.Redrive
            ? QueueDeadLetterSelectedProtocol.Three : QueueDeadLetterSelectedProtocol.Two;
        var expectedCounters = new QueueCounters(settled ? QueueDeadLetterSelectedProtocol.One : QueueDeadLetterSelectedProtocol.Two,
            settled ? retainedBytes : allBytes, QueueDeadLetterSelectedProtocol.Zero, QueueDeadLetterSelectedProtocol.Zero,
            readySequence, settled ? QueueDeadLetterSelectedProtocol.One : QueueDeadLetterSelectedProtocol.Two,
            settled ? retainedBytes : allBytes, QueueDeadLetterSelectedProtocol.Two);
        await Assert.That(store.Read(view => view.GetRecord<QueueCounters>(QueueDeadLetterSelectedSetup.Key(
            state.Lane, QueueDeadLetterSelectedProtocol.Counters)))).IsEqualTo(expectedCounters);
        await IndicesAsync(store, state, settled);
        if (healthy)
        {
            var metadata = new MessageMetadata(QueueDeadLetterSelectedProtocol.Healthy, MessageState.Acked,
                QueueDeadLetterSelectedProtocol.One, QueueDeadLetterSelectedProtocol.Three, HealthySequence(state), null, null,
                LeaseVersion: QueueDeadLetterSelectedProtocol.One);
            await MessageAsync(database, store, state.Lane, metadata, null);
        }
    }

    internal static int HealthySequence(QueueDeadLetterSelectedState state)
        => state.Kind == QueueDeadLetterSelectedOperation.Redrive ? QueueDeadLetterSelectedProtocol.Four : QueueDeadLetterSelectedProtocol.Three;

    private static MessageMetadata Terminal(MessageMetadata original, QueueDeadLetterSelectedOperation kind)
        => kind == QueueDeadLetterSelectedOperation.Redrive
            ? original with
            {
                State = MessageState.Acked,
                Attempts = QueueDeadLetterSelectedProtocol.One,
                StateVersion = QueueDeadLetterSelectedProtocol.Six,
                ReadySequence = QueueDeadLetterSelectedProtocol.Three,
                LeaseVersion = QueueDeadLetterSelectedProtocol.Three,
                DeliveryGeneration = QueueDeadLetterSelectedProtocol.Two,
                SafeFailureCode = null,
                ParkedSequence = QueueDeadLetterSelectedProtocol.Zero
            }
            : original with
            {
                State = MessageState.Cancelled,
                StateVersion = QueueDeadLetterSelectedProtocol.Four,
                LeaseVersion = QueueDeadLetterSelectedProtocol.Two,
                DeliveryGeneration = QueueDeadLetterSelectedProtocol.Two,
                ParkedSequence = QueueDeadLetterSelectedProtocol.Zero
            };

    private static async Task MessageAsync(DatabaseEngine database, ZoneTreeStore store, QueueLaneRef lane,
        MessageMetadata metadata, MessageBody? body)
    {
        var actual = database.InspectMessage(QueueDeadLetterSelectedProtocol.Administrator, lane, metadata.Id)!;
        await Assert.That(actual).IsEqualTo(new MessageInspection(metadata, body?.PayloadJson, body?.HeadersJson));
        await Assert.That(store.Read(view => view.GetRecord<MessageMetadata>(QueueDeadLetterSelectedSetup.Key(
            lane, QueueDeadLetterSelectedProtocol.Metadata, metadata.Id)))).IsEqualTo(metadata);
        await Assert.That(store.Read(view => view.GetRecord<MessageBody>(QueueDeadLetterSelectedSetup.Key(
            lane, QueueDeadLetterSelectedProtocol.Body, metadata.Id)))).IsEqualTo(body);
    }

    private static async Task IndicesAsync(ZoneTreeStore store, QueueDeadLetterSelectedState state, bool settled)
    {
        foreach (var space in new[] { QueueDeadLetterSelectedProtocol.Ready, QueueDeadLetterSelectedProtocol.Scheduled,
            QueueDeadLetterSelectedProtocol.Lease, QueueDeadLetterSelectedProtocol.Pending })
        {
            await PageAsync(store, state.Lane, space, []);
        }
        var dead = new List<string>();
        var parked = new List<string>();
        foreach (var (id, sequence) in new[]
        {
            (QueueDeadLetterSelectedProtocol.Original, QueueDeadLetterSelectedProtocol.One),
            (QueueDeadLetterSelectedProtocol.Foreign, QueueDeadLetterSelectedProtocol.Two)
        })
        {
            if (settled && id == state.Target)
            {
                continue;
            }
            dead.Add(Row(QueueDeadLetterSelectedSetup.Key(state.Lane, QueueDeadLetterSelectedProtocol.DeadLetter, id), id));
            parked.Add(Row(QueueDeadLetterSelectedSetup.Key(state.Lane, QueueDeadLetterSelectedProtocol.Parked, sequence, id),
                new QueueDeadLetterReference(id, sequence, QueueDeadLetterSelectedProtocol.One, QueueDeadLetterSelectedProtocol.Exhausted)));
        }
        await PageAsync(store, state.Lane, QueueDeadLetterSelectedProtocol.DeadLetter, dead.ToArray());
        await PageAsync(store, state.Lane, QueueDeadLetterSelectedProtocol.Parked, parked.ToArray());
    }

    private static string Row<T>(byte[] key, T value) => Convert.ToHexString(key) + QueueDeadLetterSelectedProtocol.Separator
        + Convert.ToHexString(OrleansSerialization.Serialize(value));
    private static async Task PageAsync(ZoneTreeStore store, QueueLaneRef lane, string space, string[] expected)
    {
        var page = store.Read(view => view.Scan(QueueDeadLetterSelectedSetup.Key(lane, space), QueueDeadLetterSelectedProtocol.Four));
        await Assert.That(page.HasMore).IsFalse();
        var actual = page.Records.Select(row => Convert.ToHexString(row.Key.Span) + QueueDeadLetterSelectedProtocol.Separator
            + Convert.ToHexString(row.Value.Span)).ToArray();
        await Assert.That(actual).IsEquivalentTo(expected, CollectionOrdering.Matching);
    }
}
