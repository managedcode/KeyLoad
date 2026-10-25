using System.Globalization;
using System.Text;
using KeyLoad.Core.Messaging;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.Messaging;

internal static class QueueAccountingFaultAssertions
{
    private const int NoResultError = -1;
    private const string UnexpectedReturnCounterDetail =
        "Unexpected execution queue return: error={0}; storedMessages={1}; storedBytes={2}; inFlightMessages={3}; inFlightBytes={4}.";
    private static readonly CompositeFormat UnexpectedReturnCounterFormat = CompositeFormat.Parse(UnexpectedReturnCounterDetail);

    internal static async Task RefusedAsync(DatabaseEngine database, ZoneTreeStore store,
        QueueAccountingFaultState state, string[] image, ErrorCode expectedError)
    {
        var position = store.Position;
        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
        {
            var returned = database.Apply(state.Operation);
            var counters = QueueAccountingFaultSetup.Counters(store, state.Lane);
            var code = Errors.Code(returned.Problem).Value is { } error ? (int)error : NoResultError;
            throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, UnexpectedReturnCounterFormat,
                code, counters.StoredMessages, counters.StoredBytes, counters.InFlightMessages, counters.InFlightBytes));
        });
        await Assert.That(failure.Code).IsEqualTo(expectedError);
        await Assert.That(failure.Message).IsEqualTo(QueueLifecycleProtocol.InvalidCounter);
        await Assert.That(store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(store)).IsEquivalentTo(image, CollectionOrdering.Matching);
        await Assert.That(database.GetDocument(QueueAccountingFaultProtocol.Root,
            new(state.Lane.Partition, QueueAccountingFaultProtocol.Collection, QueueAccountingFaultProtocol.Document))).IsNull();
        var outcome = store.Read(view => view.GetRecord<StoredOutcome>(KeySpace.PartitionOutcome(
            state.Lane.Partition, QueueAccountingFaultProtocol.Root, state.Operation.CommandId)));
        await Assert.That(outcome).IsNull();
    }

    internal static async Task CompletedAsync(DatabaseEngine database, ZoneTreeStore store,
        QueueAccountingFaultState state, Result<object> result)
    {
        var expected = ExpectedMetadata(state);
        var retained = expected.State is MessageState.Leased or MessageState.Scheduled;
        var inspection = database.InspectMessage(QueueAccountingFaultProtocol.Root, state.Lane, state.Message.MessageId)!;
        await Assert.That(inspection.Metadata).IsEqualTo(expected);
        await Assert.That(inspection.PayloadJson).IsEqualTo(retained ? QueueAccountingFaultProtocol.Payload : null);
        await Assert.That(inspection.HeadersJson).IsEqualTo(retained ? QueueAccountingFaultProtocol.Headers : null);
        var body = store.Read(view => view.GetRecord<MessageBody>(KeySpace.Partition(QueueAccountingFaultProtocol.BodySpace,
            state.Lane.Partition, state.Lane.Queue, state.Message.MessageId)));
        await Assert.That(body).IsEqualTo(retained ? new MessageBody(state.Message.MessageId,
            QueueAccountingFaultProtocol.Payload, QueueAccountingFaultProtocol.Headers, null, JsonData.Fingerprint(state.Message)) : null);
        var sequence = state.Kind == QueueAccountingOperation.Reclaim ? QueueAccountingFaultProtocol.Two : state.Counters.NextReadySequence;
        var flight = state.Kind == QueueAccountingOperation.Reclaim;
        await Assert.That(QueueAccountingFaultSetup.Counters(store, state.Lane)).IsEqualTo(new QueueCounters(
            retained ? QueueAccountingFaultProtocol.One : QueueAccountingFaultProtocol.Zero,
            retained ? state.BodyBytes : QueueAccountingFaultProtocol.Zero,
            flight ? QueueAccountingFaultProtocol.One : QueueAccountingFaultProtocol.Zero,
            flight ? state.BodyBytes : QueueAccountingFaultProtocol.Zero, sequence));
        await ResultAsync(database, state, result, expected);
    }

    private static async Task ResultAsync(DatabaseEngine database, QueueAccountingFaultState state,
        Result<object> result, MessageMetadata expected)
    {
        await Assert.That(Errors.Code(result.Problem).Value).IsNull();
        if (state.Operation.Value!.Kind == OperationKind.Receive)
        {
            var received = result.Get<ReceiveResult>();
            await Assert.That(received.RequestId).IsEqualTo(state.Operation.CommandId);
            if (state.Kind != QueueAccountingOperation.Reclaim)
            {
                await Assert.That(received.Deliveries).IsEmpty();
                return;
            }
            var delivery = await Assert.That(received.Deliveries).HasSingleItem();
            await Assert.That(delivery).IsEqualTo(new Delivery(state.Message.MessageId, QueueAccountingFaultProtocol.Payload,
                QueueAccountingFaultProtocol.Headers, delivery.Token, expected.LeaseVersion, expected.LeaseUntil!.Value,
                expected.Attempts, expected.DeliveryGeneration));
            await Assert.That(database.Verify<DeliveryClaims>(delivery.Token)).IsEqualTo(new DeliveryClaims(state.Lane,
                state.Message.MessageId, QueueAccountingFaultProtocol.Root, expected.LeaseVersion,
                expected.DeliveryGeneration, database.Store.Identity.Incarnation));
            return;
        }
        var receipt = result.Get<CommitReceipt>();
        await Assert.That(receipt.CommandId).IsEqualTo(state.Operation.CommandId);
        var mutation = new MutationReceipt(state.Kind == QueueAccountingOperation.Nack ? nameof(DeliveryAction.Nack)
            : nameof(DeliveryAction.Ack), state.Lane.Queue, state.Message.MessageId, QueueAccountingFaultProtocol.Three);
        MutationReceipt[] expectedMutations = state.Kind == QueueAccountingOperation.Processing
            ? [new(QueueAccountingFaultProtocol.PutKind, QueueAccountingFaultProtocol.Collection, QueueAccountingFaultProtocol.Document, QueueAccountingFaultProtocol.One), mutation]
            : [mutation];
        await Assert.That(receipt.Mutations).IsEquivalentTo(expectedMutations, CollectionOrdering.Matching);
        if (state.Kind == QueueAccountingOperation.Processing)
        {
            var document = database.GetDocument(QueueAccountingFaultProtocol.Root,
                new(state.Lane.Partition, QueueAccountingFaultProtocol.Collection, QueueAccountingFaultProtocol.Document))!;
            await Assert.That(document.Json).IsEqualTo(QueueAccountingFaultProtocol.Payload);
            await Assert.That(document.Revision).IsEqualTo(QueueAccountingFaultProtocol.One);
        }
    }

    private static MessageMetadata ExpectedMetadata(QueueAccountingFaultState state) => state.Kind switch
    {
        QueueAccountingOperation.Ack or QueueAccountingOperation.Processing => state.Metadata with
        {
            State = MessageState.Acked,
            StateVersion = QueueAccountingFaultProtocol.Three,
            LeaseOwner = null,
            LeaseUntil = null
        },
        QueueAccountingOperation.Nack => state.Metadata with
        {
            State = MessageState.Scheduled,
            StateVersion = QueueAccountingFaultProtocol.Three,
            LeaseOwner = null,
            LeaseUntil = null,
            NotBefore = state.Start.AddMilliseconds(QueueAccountingFaultProtocol.RetryMilliseconds),
            SafeFailureCode = QueueAccountingFaultProtocol.RetryReason
        },
        QueueAccountingOperation.Reclaim => state.Metadata with
        {
            StateVersion = QueueAccountingFaultProtocol.Four,
            Attempts = QueueAccountingFaultProtocol.Two,
            ReadySequence = QueueAccountingFaultProtocol.Two,
            LeaseVersion = QueueAccountingFaultProtocol.Two,
            LeaseUntil = new DateTimeOffset(state.Operation.Timestamp).AddSeconds(QueueAccountingFaultProtocol.LeaseSeconds)
        },
        QueueAccountingOperation.ReadyExpiry or QueueAccountingOperation.ScheduledExpiry => state.Metadata with
        {
            State = MessageState.Expired,
            StateVersion = QueueAccountingFaultProtocol.Two
        },
        _ => throw new ArgumentOutOfRangeException(nameof(state))
    };
}
