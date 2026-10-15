using KeyLoad.Core;
using KeyLoad.Core.Features.Messaging;
using static KeyLoad.UnitTests.Features.Messaging.RemoteTransferCapacityAuthorityProtocol;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class RemoteTransferCapacityAuthoritySeed
{
    internal static async Task<RemoteTransferCapacityAuthorityTransfer> CreateAsync(RemoteTransferDatabase fixture,
        string message, CommandRequest? prepared = null)
    {
        var mutation = prepared?.Mutations.OfType<CreateQueueTransfer>().Single()
            ?? new CreateQueueTransfer(fixture.SourceQueue, Guid.NewGuid(), fixture.DestinationQueue,
                new(fixture.DestinationQueue.Queue, message, Payload, Headers));
        var request = prepared ?? new CommandRequest(Guid.NewGuid(), fixture.SourcePartition, [mutation]);
        var created = await RemoteTransferCapacityAuthorityValues.SubmitAsync(fixture, request);
        var claims = new RemoteTransferIntentClaims(RemoteTransferProtocol.IntentPurpose, fixture.Store.Identity.Incarnation,
            fixture.SourceQueue, mutation.TransferId, fixture.DestinationQueue, RemoteTransferDatabase.RootPrincipal,
            mutation.Message, JsonData.Fingerprint(mutation.Message));
        var actual = fixture.Database.InspectQueueTransfer(RemoteTransferDatabase.RootPrincipal, fixture.SourceQueue, mutation.TransferId);
        await Assert.That(actual).IsNotNull();
        await Assert.That(string.IsNullOrEmpty(actual!.IntentToken)).IsFalse();
        var expected = new QueueTransferInspection(fixture.SourceQueue, mutation.TransferId, fixture.DestinationQueue,
            QueueTransferState.OutputPending, actual.IntentToken, null);
        await RemoteTransferCapacityAuthorityValues.EqualAsync(actual, expected);
        await RemoteTransferCapacityAuthorityValues.EqualAsync(fixture.Database.Verify<RemoteTransferIntentClaims>(expected.IntentToken), claims);
        return new(mutation, request, created, expected);
    }

    internal static async Task<(CommandRequest Request, OperationResult Result, QueueTransferReceiptInspection Proof)> AcceptAsync(
        RemoteTransferDatabase fixture, RemoteTransferCapacityAuthorityTransfer transfer, CommandRequest? prepared = null)
    {
        var request = prepared ?? new CommandRequest(Guid.NewGuid(), fixture.DestinationPartition,
            [new AcceptQueueTransfer(fixture.DestinationQueue, transfer.Intent.IntentToken)]);
        var result = await RemoteTransferCapacityAuthorityValues.SubmitAsync(fixture, request);
        var token = result.Get<CommitReceipt>().Token;
        var claims = new RemoteTransferReceiptClaims(RemoteTransferProtocol.ReceiptPurpose, fixture.Store.Identity.Incarnation,
            fixture.SourceQueue, transfer.Mutation.TransferId, fixture.DestinationQueue, RemoteTransferDatabase.RootPrincipal,
            JsonData.Fingerprint(transfer.Mutation.Message), token);
        var actual = fixture.Database.InspectQueueTransferReceipt(RemoteTransferDatabase.RootPrincipal,
            fixture.DestinationQueue, fixture.SourceQueue, transfer.Mutation.TransferId);
        await Assert.That(actual).IsNotNull();
        await Assert.That(string.IsNullOrEmpty(actual!.ReceiptToken)).IsFalse();
        var expected = new QueueTransferReceiptInspection(fixture.DestinationQueue, fixture.SourceQueue,
            transfer.Mutation.TransferId, actual.ReceiptToken, token);
        await RemoteTransferCapacityAuthorityValues.EqualAsync(actual, expected);
        await RemoteTransferCapacityAuthorityValues.EqualAsync(fixture.Database.Verify<RemoteTransferReceiptClaims>(expected.ReceiptToken), claims);
        return (request, result, expected);
    }

    internal static CommandRequest Fresh(RemoteTransferDatabase fixture, bool source, RemoteTransferCapacityAuthorityTransfer pending)
    {
        Mutation mutation = source ? new CreateQueueTransfer(fixture.SourceQueue, Guid.NewGuid(), fixture.DestinationQueue,
                new(fixture.DestinationQueue.Queue, RemoteTransferCapacityAuthorityProtocol.Fresh, Payload, Headers))
            : new AcceptQueueTransfer(fixture.DestinationQueue, pending.Intent.IntentToken);
        return new(Guid.NewGuid(), source ? fixture.SourcePartition : fixture.DestinationPartition,
            [new PutDocument(Documents, FreshDocument, Payload), mutation]);
    }
}
