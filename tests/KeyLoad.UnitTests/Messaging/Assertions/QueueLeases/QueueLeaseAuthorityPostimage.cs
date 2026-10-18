using ManagedCode.Communication;
using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueLeaseAuthorityPostimage
{
    internal static async Task RequireAsync(DatabaseEngine database, ZoneTreeStore store,
        QueueLeaseAuthorityState state, Result<object> result, bool healthySettled = false)
    {
        var metadata = Expected(state);
        var retained = state.Kind is QueueLeaseAuthorityOperation.Renew or QueueLeaseAuthorityOperation.Nack;
        var inspection = database.InspectMessage(QueueLeaseAuthorityProtocol.Root, state.Lane, state.Message.MessageId)!;
        await Assert.That(inspection.Metadata).IsEqualTo(metadata);
        await Assert.That(inspection.PayloadJson).IsEqualTo(retained ? QueueLeaseAuthorityProtocol.Payload : null);
        await Assert.That(inspection.HeadersJson).IsEqualTo(retained ? QueueLeaseAuthorityProtocol.Headers : null);
        var body = store.Read(view => view.GetRecord<MessageBody>(KeySpace.Partition(QueueLeaseAuthorityProtocol.BodySpace,
            state.Lane.Partition, state.Lane.Queue, state.Message.MessageId)));
        await Assert.That(body).IsEqualTo(retained ? new MessageBody(state.Message.MessageId, QueueLeaseAuthorityProtocol.Payload,
            QueueLeaseAuthorityProtocol.Headers, null, JsonData.Fingerprint(state.Message)) : null);
        await CountersAsync(store, state, retained, healthySettled);
        await QueueLeaseAuthorityIndices.RequireAsync(store, state, metadata);
        var receipt = result.Get<CommitReceipt>();
        await Assert.That(receipt.CommandId).IsEqualTo(state.Original.Id);
        var kind = state.Kind == QueueLeaseAuthorityOperation.Processing ? nameof(DeliveryAction.Ack) : state.Kind.ToString();
        var settled = new MutationReceipt(kind, state.Lane.Queue, state.Message.MessageId, QueueLeaseAuthorityProtocol.Three);
        MutationReceipt[] expected = state.Kind == QueueLeaseAuthorityOperation.Processing
            ? [new(QueueLeaseAuthorityProtocol.PutKind, QueueLeaseAuthorityProtocol.Collection,
                QueueLeaseAuthorityProtocol.Document, QueueLeaseAuthorityProtocol.One), settled] : [settled];
        await Assert.That(receipt.Mutations).IsEquivalentTo(expected, CollectionOrdering.Matching);
        await ProcessingAsync(database, state, result);
    }

    private static async Task CountersAsync(ZoneTreeStore store, QueueLeaseAuthorityState state, bool retained, bool healthySettled)
    {
        var leased = state.Kind == QueueLeaseAuthorityOperation.Renew;
        await Assert.That(QueueLeaseAuthoritySetup.Counters(store, state.Lane)).IsEqualTo(new QueueCounters(
            retained ? QueueLeaseAuthorityProtocol.One : QueueLeaseAuthorityProtocol.Zero,
            retained ? state.BodyBytes : QueueLeaseAuthorityProtocol.Zero,
            leased ? QueueLeaseAuthorityProtocol.One : QueueLeaseAuthorityProtocol.Zero,
            leased ? state.BodyBytes : QueueLeaseAuthorityProtocol.Zero,
            healthySettled ? QueueLeaseAuthorityProtocol.Two : QueueLeaseAuthorityProtocol.One));
    }

    private static async Task ProcessingAsync(DatabaseEngine database, QueueLeaseAuthorityState state, Result<object> result)
    {
        if (state.Kind != QueueLeaseAuthorityOperation.Processing)
        {
            return;
        }
        var request = JsonSerializer.Deserialize<ProcessingRequest>(state.Original.PayloadJson, JsonDefaults.Options)!;
        var document = database.GetDocument(QueueLeaseAuthorityProtocol.Root,
            new(state.Lane.Partition, QueueLeaseAuthorityProtocol.Collection, QueueLeaseAuthorityProtocol.Document))!;
        await Assert.That(document.Reference).IsEqualTo(new EntityRef(state.Lane.Partition,
            QueueLeaseAuthorityProtocol.Collection, QueueLeaseAuthorityProtocol.Document));
        await Assert.That(document.Json).IsEqualTo(QueueLeaseAuthorityProtocol.Payload);
        await Assert.That(document.Revision).IsEqualTo(QueueLeaseAuthorityProtocol.One);
        await Assert.That(document.Redacted).IsFalse();
        await Assert.That(document.RedactedFields).IsEmpty();
        var inbox = database.Store.Read(view => view.GetRecord<InboxRecord>(KeySpace.Partition(
            QueueLeaseAuthorityProtocol.InboxSpace, state.Lane.Partition, state.Lane.Queue,
            state.Message.MessageId, QueueLeaseAuthorityProtocol.Handler, QueueLeaseAuthorityProtocol.One)))!;
        var fingerprint = JsonData.Fingerprint(new
        {
            request.Lane,
            MessageId = state.Message.MessageId,
            request.HandlerScope,
            request.ExecutionGeneration,
            request.Effects,
            Id = QueueLeaseAuthorityProtocol.Root
        });
        await Assert.That(inbox.Fingerprint).IsEqualTo(fingerprint);
        await Assert.That(JsonSerializer.Serialize(inbox.Receipt, JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(result.Get<CommitReceipt>(), JsonDefaults.Options));
    }

    internal static MessageMetadata Expected(QueueLeaseAuthorityState state) => state.Kind switch
    {
        QueueLeaseAuthorityOperation.Ack or QueueLeaseAuthorityOperation.Processing => state.Metadata with
        {
            State = MessageState.Acked,
            StateVersion = QueueLeaseAuthorityProtocol.Three,
            LeaseOwner = null,
            LeaseUntil = null
        },
        QueueLeaseAuthorityOperation.Renew => state.Metadata with
        {
            StateVersion = QueueLeaseAuthorityProtocol.Three,
            LeaseUntil = state.Original.EvaluatedAt.AddSeconds(QueueLeaseAuthorityProtocol.LeaseSeconds)
        },
        QueueLeaseAuthorityOperation.Nack => state.Metadata with
        {
            State = MessageState.Scheduled,
            StateVersion = QueueLeaseAuthorityProtocol.Three,
            LeaseOwner = null,
            LeaseUntil = null,
            NotBefore = state.Original.EvaluatedAt.AddMilliseconds(QueueLeaseAuthorityProtocol.RetryMilliseconds),
            SafeFailureCode = QueueLeaseAuthorityProtocol.RetryReason
        },
        _ => throw new ArgumentOutOfRangeException(nameof(state))
    };
}
