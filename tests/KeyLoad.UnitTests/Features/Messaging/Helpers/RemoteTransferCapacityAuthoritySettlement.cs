using KeyLoad.UnitTests.Features.ResourceExecution;
using static KeyLoad.UnitTests.Features.Messaging.RemoteTransferCapacityAuthorityProtocol;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class RemoteTransferCapacityAuthoritySettlement
{
    internal static async Task<RemoteTransferCapacityAuthorityCompleted> FinishAsync(RemoteTransferDatabase fixture,
        RemoteTransferCapacityAuthorityTransfer transfer, long sequence, CommandRequest? preparedAccept = null)
    {
        var accepted = await RemoteTransferCapacityAuthoritySeed.AcceptAsync(fixture, transfer, preparedAccept);
        return await CompleteAcceptedAsync(fixture, transfer, sequence, accepted);
    }

    internal static async Task<RemoteTransferCapacityAuthorityCompleted> CompleteAcceptedAsync(RemoteTransferDatabase fixture,
        RemoteTransferCapacityAuthorityTransfer transfer, long sequence,
        (CommandRequest Request, OperationResult Result, QueueTransferReceiptInspection Proof) accepted)
    {
        var complete = new CommandRequest(Guid.NewGuid(), fixture.SourcePartition,
            [new CompleteQueueTransfer(fixture.SourceQueue, transfer.Mutation.TransferId, accepted.Proof.ReceiptToken)]);
        var completed = await RemoteTransferCapacityAuthorityValues.SubmitAsync(fixture, complete);
        await RemoteTransferCapacityAuthorityValues.EqualAsync(fixture.Database.InspectQueueTransfer(RemoteTransferDatabase.RootPrincipal,
            fixture.SourceQueue, transfer.Mutation.TransferId), transfer.Intent with
            {
                State = QueueTransferState.Delivered,
                ReceiptToken = accepted.Proof.ReceiptToken
            });
        var received = await ReceiveAsync(fixture, transfer);
        var delivery = received.Result.Get<ReceiveResult>().Deliveries.Single();
        var ack = new DeliveryCommand(Guid.NewGuid(), fixture.DestinationQueue, delivery.Token, DeliveryAction.Ack);
        var placement = fixture.Database.ReadAtomicPartitionPlacement(RemoteTransferDatabase.RootPrincipal, new(First, fixture.DestinationPartition));
        var token = new CommitToken(placement.Incarnation, fixture.DestinationPartition.AtomicPartitionId,
            checked(fixture.Store.Position + First), placement.PlacementEpoch);
        var acked = fixture.Apply(OperationKind.Delivery, ack, id: ack.CommandId);
        await NativeReplayResultAssertions.Same<CommitReceipt>(acked, new(null)
        {
            NativeValue = new CommitReceipt(ack.CommandId, token,
                [new(nameof(DeliveryAction.Ack), fixture.DestinationQueue.Queue, transfer.Mutation.Message.MessageId, Third)], DurabilityProfile.ProcessDurable)
        });
        var result = new RemoteTransferCapacityAuthorityCompleted(transfer, accepted.Request, accepted.Result, accepted.Proof,
            complete, completed, received.Request, received.Result, ack, acked, sequence);
        await RemoteTransferCapacityAuthorityImages.CompletedAsync(fixture, result);
        return result;
    }

    private static async Task<(ReceiveRequest Request, OperationResult Result)> ReceiveAsync(RemoteTransferDatabase fixture,
        RemoteTransferCapacityAuthorityTransfer transfer)
    {
        var request = new ReceiveRequest(Guid.NewGuid(), fixture.DestinationQueue, MaxMessages: First, LeaseSeconds: LeaseSeconds);
        var time = TimeProvider.System.GetUtcNow();
        var placement = fixture.Database.ReadAtomicPartitionPlacement(RemoteTransferDatabase.RootPrincipal, new(First, fixture.DestinationPartition));
        var token = new CommitToken(placement.Incarnation, fixture.DestinationPartition.AtomicPartitionId,
            checked(fixture.Store.Position + First), placement.PlacementEpoch);
        var claims = new DeliveryClaims(fixture.DestinationQueue, transfer.Mutation.Message.MessageId,
            RemoteTransferDatabase.RootPrincipal, First, First, fixture.Store.Identity.Incarnation);
        var expected = new ReceiveResult(request.RequestId,
            [new(transfer.Mutation.Message.MessageId, Payload, Headers, fixture.Database.Sign(claims), First,
                time.AddSeconds(LeaseSeconds), First, First)], token);
        var result = fixture.Apply(OperationKind.Receive, request, id: request.RequestId, time: time);
        await NativeReplayResultAssertions.Same<ReceiveResult>(result, new(null)
        {
            NativeValue = expected
        });
        await RemoteTransferCapacityAuthorityValues.EqualAsync(fixture.Database.Verify<DeliveryClaims>(expected.Deliveries.Single().Token), claims);
        return (request, result);
    }
}
