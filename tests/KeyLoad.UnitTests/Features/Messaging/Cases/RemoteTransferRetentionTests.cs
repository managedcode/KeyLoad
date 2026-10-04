using System.Text;
using System.Text.Json;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class RemoteTransferRetentionTests
{
    private const string Payload = "{\"retained\":true}";
    private const string FirstId = "transfer-one";
    private const string SecondId = "transfer-two";
    private const string SourceMessageId = "disposable-source-message";
    private const int MaxRetainedRecords = 1;

    [Test]
    public async Task PerLaneRecordCapsRejectExcessSourceAndDestinationStateAtomically()
    {
        using var fixture = new RemoteTransferDatabase(new() { MaxScanRecords = MaxRetainedRecords });
        var otherSource = OtherSource(fixture);
        var first = Create(fixture, fixture.SourceQueue, FirstId);
        var second = Create(fixture, otherSource, SecondId);
        fixture.Commit(fixture.SourcePartition, first);
        fixture.Commit(otherSource.Partition, second);

        var accepted = fixture.Database.InspectQueueTransfer(RemoteTransferDatabase.RootPrincipal,
            fixture.SourceQueue, first.TransferId)!;
        var overflow = Create(fixture, fixture.SourceQueue, SecondId);
        var rejectedSource = fixture.Apply(OperationKind.Batch,
            new CommandRequest(Guid.NewGuid(), fixture.SourcePartition,
                [overflow]));
        await Assert.That(rejectedSource.Error).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(fixture.SourceCapacity(fixture.SourceQueue).StoredRecords).IsEqualTo(MaxRetainedRecords);
        await Assert.That(fixture.Database.InspectQueueTransfer(RemoteTransferDatabase.RootPrincipal,
            fixture.SourceQueue, first.TransferId)!.IntentToken)
            .IsEqualTo(accepted.IntentToken);
        await Assert.That(fixture.Database.InspectQueueTransfer(RemoteTransferDatabase.RootPrincipal,
            fixture.SourceQueue, overflow.TransferId)).IsNull();

        fixture.Commit(fixture.DestinationPartition,
            new AcceptQueueTransfer(fixture.DestinationQueue, accepted.IntentToken));
        var secondIntent = fixture.Database.InspectQueueTransfer(RemoteTransferDatabase.RootPrincipal,
            otherSource, second.TransferId)!;
        var rejectedTarget = fixture.Apply(OperationKind.Batch,
            new CommandRequest(Guid.NewGuid(), fixture.DestinationPartition,
                [new AcceptQueueTransfer(fixture.DestinationQueue, secondIntent.IntentToken)]));
        await Assert.That(rejectedTarget.Error).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(fixture.TargetCapacity(fixture.DestinationQueue).StoredRecords).IsEqualTo(MaxRetainedRecords);
        await Assert.That(fixture.Database.InspectQueueTransferReceipt(RemoteTransferDatabase.RootPrincipal,
            fixture.DestinationQueue, otherSource, second.TransferId)).IsNull();
        await Assert.That(fixture.Database.InspectMessage(RemoteTransferDatabase.RootPrincipal,
            fixture.DestinationQueue, SecondId)).IsNull();
    }

    [Test]
    public async Task ByteCapIncludesNativeReceiptReservationAndDoesNotEvictAcceptedIntent()
    {
        using var measured = new RemoteTransferDatabase();
        var first = Create(measured, measured.SourceQueue, FirstId);
        measured.Commit(measured.SourcePartition, first);
        var transferId = first.TransferId;
        var stored = measured.Database.Store.Read(view => view.GetRecord<RemoteTransferIntentRecord>(
            RemoteTransferStorage.IntentKey(measured.SourceQueue, transferId)))!;
        var byteLimit = checked(stored.ReceiptReservationBytes - 1);
        var next = Create(measured, measured.SourceQueue, SecondId);
        var requestBytes = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(
            new CommandRequest(Guid.NewGuid(), measured.SourcePartition, [next]), JsonDefaults.Options));
        await Assert.That(stored.ReceiptReservationBytes).IsGreaterThan(byteLimit);
        await Assert.That(requestBytes).IsLessThan(byteLimit);

        using var limited = new RemoteTransferDatabase(measured.Database.Limits with { MaxBatchBytes = byteLimit });
        var limitedIntent = Create(limited, limited.SourceQueue, SecondId);
        var failure = limited.Apply(OperationKind.Batch,
            new CommandRequest(Guid.NewGuid(), limited.SourcePartition, [limitedIntent]));

        await Assert.That(failure.Error).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(limited.SourceCapacity(limited.SourceQueue).StoredRecords).IsEqualTo(0);
        await Assert.That(limited.SourceCapacity(limited.SourceQueue).StoredBytes).IsEqualTo(0);
        await Assert.That(limited.Database.InspectQueueTransfer(RemoteTransferDatabase.RootPrincipal,
            limited.SourceQueue, limitedIntent.TransferId)).IsNull();
    }

    [Test]
    public async Task SourceByteCapAllowsExactRetainedStateAndRejectsAnAdditionalIntent()
    {
        using var fixture = new RemoteTransferDatabase();
        var first = Create(fixture, fixture.SourceQueue, FirstId);
        fixture.Commit(fixture.SourcePartition, first);
        var firstIntent = fixture.Database.InspectQueueTransfer(RemoteTransferDatabase.RootPrincipal,
            fixture.SourceQueue, first.TransferId)!;
        var retainedBytes = fixture.SourceCapacity(fixture.SourceQueue).StoredBytes;
        var next = Create(fixture, fixture.SourceQueue, SecondId);
        var requestBytes = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(
            new CommandRequest(Guid.NewGuid(), fixture.SourcePartition, [next]), JsonDefaults.Options));
        await Assert.That((long)Encoding.UTF8.GetByteCount(firstIntent.IntentToken)).IsLessThan(retainedBytes);
        await Assert.That((long)requestBytes).IsLessThan(retainedBytes);

        fixture.Reopen(fixture.Database.Limits with { MaxBatchBytes = checked((int)retainedBytes) });
        fixture.Commit(fixture.SourcePartition, first);
        var rejected = fixture.Apply(OperationKind.Batch,
            new CommandRequest(Guid.NewGuid(), fixture.SourcePartition, [next]));

        await Assert.That(rejected.Error).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(fixture.SourceCapacity(fixture.SourceQueue).StoredBytes).IsEqualTo(retainedBytes);
        await Assert.That(fixture.Database.InspectQueueTransfer(RemoteTransferDatabase.RootPrincipal,
            fixture.SourceQueue, first.TransferId)!.IntentToken).IsEqualTo(firstIntent.IntentToken);
        await Assert.That(fixture.Database.InspectQueueTransfer(RemoteTransferDatabase.RootPrincipal,
            fixture.SourceQueue, next.TransferId)).IsNull();
    }

    [Test]
    public async Task DestinationByteCapPreservesReceiptAndRejectsTheNextAtomicEnqueue()
    {
        using var fixture = new RemoteTransferDatabase();
        var otherSource = OtherSource(fixture);
        var first = Create(fixture, fixture.SourceQueue, FirstId);
        var second = Create(fixture, otherSource, SecondId);
        fixture.Commit(fixture.SourcePartition, first);
        fixture.Commit(otherSource.Partition, second);
        var firstIntent = fixture.Database.InspectQueueTransfer(RemoteTransferDatabase.RootPrincipal,
            fixture.SourceQueue, first.TransferId)!;
        var secondIntent = fixture.Database.InspectQueueTransfer(RemoteTransferDatabase.RootPrincipal,
            otherSource, second.TransferId)!;
        fixture.Commit(fixture.DestinationPartition,
            new AcceptQueueTransfer(fixture.DestinationQueue, firstIntent.IntentToken));
        var retainedBytes = fixture.TargetCapacity(fixture.DestinationQueue).StoredBytes;
        var commandBytes = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(
            new CommandRequest(Guid.NewGuid(), fixture.DestinationPartition,
                [new AcceptQueueTransfer(fixture.DestinationQueue, secondIntent.IntentToken)]), JsonDefaults.Options));
        await Assert.That((long)Encoding.UTF8.GetByteCount(secondIntent.IntentToken)).IsLessThan(retainedBytes);
        var nextReceiptBytes = RemoteTransferDatabase.MeasureTargetReceiptGrowth(FirstId, SecondId, Payload);
        var byteLimit = checked((int)Math.Max(retainedBytes, commandBytes));
        await Assert.That(commandBytes).IsLessThanOrEqualTo(byteLimit);
        await Assert.That(retainedBytes + nextReceiptBytes).IsGreaterThan((long)byteLimit);

        fixture.Reopen(fixture.Database.Limits with { MaxBatchBytes = byteLimit });
        var rejected = fixture.Apply(OperationKind.Batch,
            new CommandRequest(Guid.NewGuid(), fixture.DestinationPartition,
                [new AcceptQueueTransfer(fixture.DestinationQueue, secondIntent.IntentToken)]));

        await Assert.That(rejected.Error).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(fixture.TargetCapacity(fixture.DestinationQueue).StoredBytes).IsEqualTo(retainedBytes);
        await Assert.That(fixture.Database.InspectQueueTransferReceipt(RemoteTransferDatabase.RootPrincipal,
            fixture.DestinationQueue, otherSource, second.TransferId)).IsNull();
        await Assert.That(fixture.Database.InspectMessage(RemoteTransferDatabase.RootPrincipal,
            fixture.DestinationQueue, SecondId)).IsNull();
        await Assert.That(fixture.Database.InspectMessage(RemoteTransferDatabase.RootPrincipal,
            fixture.DestinationQueue, FirstId)!.PayloadJson).IsEqualTo(Payload);
    }

    [Test]
    public async Task SourceIntentOwnsItsPayloadAfterTheOriginalQueueBodyIsAcknowledged()
    {
        using var fixture = new RemoteTransferDatabase();
        fixture.Commit(fixture.SourcePartition, new EnqueueMessage(RemoteTransferDatabase.SourceQueueName,
            SourceMessageId, Payload));
        var transfer = Create(fixture, fixture.SourceQueue, FirstId);
        fixture.Commit(fixture.SourcePartition, transfer);
        var sourceDelivery = await Assert.That(fixture.Apply(OperationKind.Receive,
            new ReceiveRequest(Guid.NewGuid(), fixture.SourceQueue)).Get<ReceiveResult>().Deliveries).HasSingleItem();
        fixture.Apply(OperationKind.Delivery, new DeliveryCommand(Guid.NewGuid(), fixture.SourceQueue,
            sourceDelivery.Token, DeliveryAction.Ack)).Get<CommitReceipt>();

        await Assert.That(fixture.Database.InspectMessage(RemoteTransferDatabase.RootPrincipal,
            fixture.SourceQueue, SourceMessageId)!.PayloadJson).IsNull();
        var retained = fixture.Database.InspectQueueTransfer(RemoteTransferDatabase.RootPrincipal,
            fixture.SourceQueue, transfer.TransferId)!;
        var intent = fixture.Database.Verify<RemoteTransferIntentClaims>(retained.IntentToken);
        await Assert.That(intent.Message.PayloadJson).IsEqualTo(Payload);
        await Assert.That(retained.State).IsEqualTo(QueueTransferState.OutputPending);
    }

    private static QueueLaneRef OtherSource(RemoteTransferDatabase fixture)
    {
        var lane = new QueueLaneRef(new(RemoteTransferDatabase.TenantId, RemoteTransferDatabase.DatabaseId,
            RemoteTransferDatabase.Domain, RemoteTransferDatabase.OtherSourcePartitionId),
            RemoteTransferDatabase.OtherSourceQueueName);
        fixture.ConfigureQueue(lane);
        return lane;
    }

    private static CreateQueueTransfer Create(RemoteTransferDatabase fixture, QueueLaneRef source, string messageId)
        => new(source, Guid.NewGuid(), fixture.DestinationQueue,
            new(fixture.DestinationQueue.Queue, messageId, Payload));

}
