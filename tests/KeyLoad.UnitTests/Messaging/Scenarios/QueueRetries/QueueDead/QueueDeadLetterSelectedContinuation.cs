using System.Text.Json;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Messaging;

internal static class QueueDeadLetterSelectedContinuation
{
    internal static async Task<List<QueueDeadLetterSelectedSaved>> RunAsync(DatabaseEngine database,
        ZoneTreeStore store, QueueDeadLetterSelectedState state)
    {
        var history = new List<QueueDeadLetterSelectedSaved>();
        var result = QueueDeadLetterSelectedSetup.Execute(database, history, state.Original);
        var receipt = result.Get<CommitReceipt>();
        await Assert.That(receipt.CommandId).IsEqualTo(state.Original.CommandId);
        await Assert.That(receipt.Mutations).IsEquivalentTo([new MutationReceipt(
            state.Kind == QueueDeadLetterSelectedOperation.Redrive ? QueueDeadLetterSelectedProtocol.RedriveKind
                : QueueDeadLetterSelectedProtocol.CancelKind, state.Lane.Queue, state.Target, QueueDeadLetterSelectedProtocol.Four)],
            CollectionOrdering.Matching);
        if (state.Kind == QueueDeadLetterSelectedOperation.Redrive)
        {
            await ProcessAsync(database, store, state, state.Target, QueueDeadLetterSelectedProtocol.Three,
                QueueDeadLetterSelectedProtocol.Three, QueueDeadLetterSelectedProtocol.Two,
                QueueDeadLetterSelectedProtocol.Six, history);
        }
        await QueueDeadLetterSelectedImage.RequireAsync(database, store, state, settled: true);
        var command = new CommandRequest(Guid.NewGuid(), state.Lane.Partition,
            [QueueDeadLetterSelectedSetup.Message(QueueDeadLetterSelectedProtocol.Healthy)]);
        QueueDeadLetterSelectedSetup.Execute(database, history, QueueDeadLetterSelectedSetup.Create(database,
            OperationKind.Batch, command, command.CommandId, state.Time)).Get<CommitReceipt>();
        await ProcessAsync(database, store, state, QueueDeadLetterSelectedProtocol.Healthy,
            QueueDeadLetterSelectedImage.HealthySequence(state), QueueDeadLetterSelectedProtocol.One,
            QueueDeadLetterSelectedProtocol.One, QueueDeadLetterSelectedProtocol.Three, history);
        await QueueDeadLetterSelectedImage.RequireAsync(database, store, state, settled: true, healthy: true);
        return history;
    }

    private static async Task ProcessAsync(DatabaseEngine database, ZoneTreeStore store, QueueDeadLetterSelectedState state,
        string id, int readySequence, int leaseVersion, int generation, int terminalVersion,
        List<QueueDeadLetterSelectedSaved> history)
    {
        var request = new ReceiveRequest(Guid.NewGuid(), state.Lane, LeaseSeconds: QueueDeadLetterSelectedProtocol.LeaseSeconds);
        var result = QueueDeadLetterSelectedSetup.Execute(database, history, QueueDeadLetterSelectedSetup.Create(database,
            OperationKind.Receive, request, request.RequestId, state.Time));
        var delivery = await Assert.That(result.Get<ReceiveResult>().Deliveries).HasSingleItem();
        await Assert.That(delivery).IsEqualTo(new Delivery(id, QueueDeadLetterSelectedProtocol.Payload,
            QueueDeadLetterSelectedProtocol.Headers, delivery.Token, leaseVersion,
            state.Time.AddSeconds(QueueDeadLetterSelectedProtocol.LeaseSeconds), QueueDeadLetterSelectedProtocol.One, generation));
        var leased = new MessageMetadata(id, MessageState.Leased, QueueDeadLetterSelectedProtocol.One,
            terminalVersion - QueueDeadLetterSelectedProtocol.One, readySequence, null, null,
            QueueDeadLetterSelectedProtocol.Administrator, leaseVersion, delivery.LeaseUntil, generation);
        await Assert.That(database.InspectMessage(QueueDeadLetterSelectedProtocol.Administrator, state.Lane, id))
            .IsEqualTo(new MessageInspection(leased, QueueDeadLetterSelectedProtocol.Payload, QueueDeadLetterSelectedProtocol.Headers));
        var processing = new ProcessingRequest(Guid.NewGuid(), state.Lane, delivery.Token,
            QueueDeadLetterSelectedProtocol.Handler, QueueDeadLetterSelectedProtocol.One,
            [new PutDocument(QueueDeadLetterSelectedProtocol.Collection, id, QueueDeadLetterSelectedProtocol.Payload)]);
        var completed = QueueDeadLetterSelectedSetup.Execute(database, history, QueueDeadLetterSelectedSetup.Create(database,
            OperationKind.Processing, processing, processing.CommandId, state.Time));
        var receipt = completed.Get<CommitReceipt>();
        await Assert.That(receipt.CommandId).IsEqualTo(processing.CommandId);
        await Assert.That(receipt.Mutations).IsEquivalentTo([
            new MutationReceipt(QueueDeadLetterSelectedProtocol.PutKind, QueueDeadLetterSelectedProtocol.Collection, id, QueueDeadLetterSelectedProtocol.One),
            new MutationReceipt(nameof(DeliveryAction.Ack), state.Lane.Queue, id, terminalVersion)], CollectionOrdering.Matching);
        await EffectsAsync(database, store, processing, id, receipt, state.DocumentLifetime);
    }

    internal static async Task EffectsAsync(DatabaseEngine database, ZoneTreeStore store,
        ProcessingRequest processing, string id, CommitReceipt receipt, EntityRefLifetime documentLifetime)
    {
        var document = database.GetDocument(QueueDeadLetterSelectedProtocol.Administrator,
            new(processing.Lane.Partition, QueueDeadLetterSelectedProtocol.Collection, id))!;
        await Assert.That(document.Reference).IsEqualTo(new EntityRef(processing.Lane.Partition, QueueDeadLetterSelectedProtocol.Collection, id)
        {
            Lifetime = documentLifetime
        });
        await Assert.That(document.Json).IsEqualTo(QueueDeadLetterSelectedProtocol.Payload);
        await Assert.That(document.Revision).IsEqualTo(QueueDeadLetterSelectedProtocol.One);
        await Assert.That(document.Redacted).IsFalse();
        await Assert.That(document.RedactedFields).IsEmpty();
        var inbox = store.Read(view => view.GetRecord<InboxRecord>(QueueDeadLetterSelectedSetup.Key(
            processing.Lane, QueueDeadLetterSelectedProtocol.Inbox, id, QueueDeadLetterSelectedProtocol.Handler, QueueDeadLetterSelectedProtocol.One)))!;
        var fingerprint = JsonData.Fingerprint(new
        {
            processing.Lane,
            MessageId = id,
            processing.HandlerScope,
            processing.ExecutionGeneration,
            processing.Effects,
            Id = QueueDeadLetterSelectedProtocol.Administrator
        });
        await Assert.That(inbox.Fingerprint).IsEqualTo(fingerprint);
        await Assert.That(JsonSerializer.Serialize(inbox.Receipt, JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(receipt, JsonDefaults.Options));
    }

    internal static async Task ColdEffectsAsync(DatabaseEngine database, ZoneTreeStore store,
        IEnumerable<QueueDeadLetterSelectedSaved> history, EntityRefLifetime documentLifetime)
    {
        foreach (var saved in history.Where(saved => saved.Operation.Value!.Kind == OperationKind.Processing))
        {
            var processing = OrleansSerialization.Deserialize<ProcessingRequest>(saved.Operation.Value!.Input.Span, ValidationProfile.PublicInputElements)!;
            var id = ((PutDocument)processing.Effects[QueueDeadLetterSelectedProtocol.Zero]).Id;
            await EffectsAsync(database, store, processing, id, saved.Result.Get<CommitReceipt>(), documentLifetime);
        }
    }
}
