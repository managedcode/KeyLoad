using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class RemoteTransferAttemptColdNative
{
    internal static byte[] Outcome(RemoteTransferDatabase fixture, Guid command, PartitionRef partition)
        => fixture.Store.Read(view =>
        {
            var selected = CommandOutcomeKeyResolver.Select(view, RemoteTransferDatabase.RootPrincipal,
                command, new(CommandOutcomeScopeKind.Partition, partition));
            return view.ReadOwnedValue(selected.Key) ?? throw new InvalidOperationException(RemoteTransferAttemptColdProtocol.Missing);
        });

    internal static RemoteTransferAcceptAttemptState State(RemoteTransferDatabase fixture, Guid transfer)
        => fixture.Store.Read(view => view.GetRecord<RemoteTransferIntentRecord>(RemoteTransferStorage.IntentKey(
            fixture.SourceQueue, transfer)))?.Attempts ?? throw new InvalidOperationException(RemoteTransferAttemptColdProtocol.Missing);

    internal static RemoteTransferCoordinationReadRequest Request(RemoteTransferDatabase fixture,
        CreateQueueTransfer transfer, string token, Guid accept, string purpose)
        => new(purpose, fixture.SourceQueue, fixture.DestinationQueue, transfer.TransferId, token,
            RemoteTransferAttemptColdProtocol.FirstGeneration, accept);

    internal static RemoteTransferCoordinationReadResult Read(RemoteTransferDatabase fixture, RemoteTransferCoordinationReadRequest request)
        => fixture.Database.ReadRemoteTransferCoordination(RemoteTransferDatabase.RootPrincipal, request, CancellationToken.None);

    internal static async Task AccountingAsync(RemoteTransferDatabase fixture, Guid transfer, long records)
    {
        var cut = fixture.Store.Read(view =>
        {
            var key = RemoteTransferStorage.IntentKey(fixture.SourceQueue, transfer);
            var record = view.GetRecord<RemoteTransferIntentRecord>(key)
                ?? throw new InvalidOperationException(RemoteTransferAttemptColdProtocol.Missing);
            var bytes = view.ReadOwnedValue(key)
                ?? throw new InvalidOperationException(RemoteTransferAttemptColdProtocol.Missing);
            return (Record: record, Bytes: bytes, Counter: RemoteTransferStorage.RequireSourceCounter(view, fixture.SourceQueue));
        });
        await Assert.That(cut.Counter.StoredRecords).IsEqualTo(records);
        await Assert.That(cut.Counter.StoredBytes).IsEqualTo(cut.Bytes.LongLength + cut.Record.ReceiptReservationBytes
            - System.Text.Encoding.UTF8.GetByteCount(cut.Record.ReceiptToken ?? RemoteTransferAttemptColdProtocol.EmptyReceipt));
    }

    internal static async Task AckAsync(RemoteTransferDatabase fixture, string message)
    {
        var receiveId = Guid.NewGuid();
        var delivery = await Assert.That(fixture.Apply(OperationKind.Receive,
            new ReceiveRequest(receiveId, fixture.DestinationQueue), id: receiveId).Get<ReceiveResult>().Deliveries).HasSingleItem();
        await Assert.That(delivery.Id).IsEqualTo(message);
        var ackId = Guid.NewGuid();
        _ = fixture.Apply(OperationKind.Delivery, new DeliveryCommand(ackId, fixture.DestinationQueue,
            delivery.Token, DeliveryAction.Ack), id: ackId).Get<CommitReceipt>();
    }
}
