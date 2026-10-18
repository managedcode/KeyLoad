using KeyLoad.Core;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueDeadLetterSelectedRetainedInbox
{
    internal static async Task SeedAsync(DatabaseEngine database, QueueLaneRef subject, DateTimeOffset time,
        List<QueueDeadLetterSelectedSaved> history)
    {
        var lane = new QueueLaneRef(subject.Partition, QueueDeadLetterSelectedProtocol.HistoryQueue);
        var message = new EnqueueMessage(lane.Queue, QueueDeadLetterSelectedProtocol.HistoryMessage,
            QueueDeadLetterSelectedProtocol.Payload, QueueDeadLetterSelectedProtocol.Headers);
        var producer = new CommandRequest(Guid.NewGuid(), lane.Partition, [message]);
        QueueDeadLetterSelectedSetup.Execute(database, history, QueueDeadLetterSelectedSetup.Create(database,
            OperationKind.Batch, producer, producer.CommandId, time)).Get<CommitReceipt>();
        var claim = new ReceiveRequest(Guid.NewGuid(), lane, LeaseSeconds: QueueDeadLetterSelectedProtocol.LeaseSeconds);
        var claimed = QueueDeadLetterSelectedSetup.Execute(database, history, QueueDeadLetterSelectedSetup.Create(database,
            OperationKind.Receive, claim, claim.RequestId, time));
        var delivery = await Assert.That(claimed.Get<ReceiveResult>().Deliveries).HasSingleItem();
        await Assert.That(delivery).IsEqualTo(new Delivery(message.MessageId, QueueDeadLetterSelectedProtocol.Payload,
            QueueDeadLetterSelectedProtocol.Headers, delivery.Token, QueueDeadLetterSelectedProtocol.One,
            time.AddSeconds(QueueDeadLetterSelectedProtocol.LeaseSeconds), QueueDeadLetterSelectedProtocol.One,
            QueueDeadLetterSelectedProtocol.One));
        var processing = new ProcessingRequest(Guid.NewGuid(), lane, delivery.Token,
            QueueDeadLetterSelectedProtocol.Handler, QueueDeadLetterSelectedProtocol.One,
            [new PutDocument(QueueDeadLetterSelectedProtocol.Collection, message.MessageId, QueueDeadLetterSelectedProtocol.Payload)]);
        QueueDeadLetterSelectedSetup.Execute(database, history, QueueDeadLetterSelectedSetup.Create(database,
            OperationKind.Processing, processing, processing.CommandId, time)).Get<CommitReceipt>();
    }

    internal static async Task RequireAsync(DatabaseEngine database, ZoneTreeStore store, QueueDeadLetterSelectedState state)
    {
        var lane = new QueueLaneRef(state.Lane.Partition, QueueDeadLetterSelectedProtocol.HistoryQueue);
        var metadata = new MessageMetadata(QueueDeadLetterSelectedProtocol.HistoryMessage, MessageState.Acked,
            QueueDeadLetterSelectedProtocol.One, QueueDeadLetterSelectedProtocol.Three,
            QueueDeadLetterSelectedProtocol.One, null, null, LeaseVersion: QueueDeadLetterSelectedProtocol.One);
        await Assert.That(database.InspectMessage(QueueDeadLetterSelectedProtocol.Administrator, lane, metadata.Id))
            .IsEqualTo(new MessageInspection(metadata, null, null));
        await Assert.That(store.Read(view => view.ReadOwnedValue(QueueDeadLetterSelectedSetup.Key(lane,
            QueueDeadLetterSelectedProtocol.Body, metadata.Id)))).IsNull();
        await Assert.That(store.Read(view => view.GetRecord<QueueCounters>(QueueDeadLetterSelectedSetup.Key(
            lane, QueueDeadLetterSelectedProtocol.Counters)))).IsEqualTo(new QueueCounters(QueueDeadLetterSelectedProtocol.Zero,
                QueueDeadLetterSelectedProtocol.Zero, QueueDeadLetterSelectedProtocol.Zero,
                QueueDeadLetterSelectedProtocol.Zero, QueueDeadLetterSelectedProtocol.One));
        foreach (var space in new[] { QueueDeadLetterSelectedProtocol.Ready, QueueDeadLetterSelectedProtocol.Lease,
            QueueDeadLetterSelectedProtocol.Scheduled, QueueDeadLetterSelectedProtocol.DeadLetter,
            QueueDeadLetterSelectedProtocol.Parked, QueueDeadLetterSelectedProtocol.Pending })
        {
            var page = store.Read(view => view.Scan(QueueDeadLetterSelectedSetup.Key(lane, space), QueueDeadLetterSelectedProtocol.One));
            await Assert.That(page.HasMore).IsFalse();
            await Assert.That(page.Records).IsEmpty();
        }
        var saved = state.History.Single(saved => saved.Operation.Value!.Kind == OperationKind.Processing);
        var processing = NativeSerialization.Deserialize<ProcessingRequest>(saved.Operation.Value!.Input.Span, NativeValidationProfile.PublicInputElements)!;
        await QueueDeadLetterSelectedContinuation.EffectsAsync(database, store, processing,
            QueueDeadLetterSelectedProtocol.HistoryMessage, saved.Result.Get<CommitReceipt>());
    }
}
