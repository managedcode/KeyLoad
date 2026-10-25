using KeyLoad.Core.Messaging;
using KeyLoad.FunctionalTests.ResourceExecution;
using ManagedCode.Communication;
using static KeyLoad.FunctionalTests.Messaging.RemoteTransferCapacityAuthorityProtocol;

namespace KeyLoad.FunctionalTests.Messaging;

internal static class RemoteTransferCapacityAuthorityValues
{
    internal static async Task EqualAsync<T>(T actual, T expected)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();

    internal static async Task<Result<object>> SubmitAsync(RemoteTransferDatabase fixture, CommandRequest request,
        long? originalAcceptPosition = null)
    {
        var placement = fixture.Database.ReadAtomicPartitionPlacement(RemoteTransferDatabase.RootPrincipal, request.Partition);
        var token = new CommitToken(placement.Incarnation, request.Partition.AtomicPartitionId,
            checked(fixture.Database.LastApplied + First));
        var expected = new CommitReceipt(request.CommandId, token, [.. request.Mutations.Select(mutation => ExpectedMutation(fixture, mutation, token, originalAcceptPosition))], DurabilityProfile.ProcessDurable, 1);
        var result = fixture.Apply(OperationKind.Batch, request);
        await ReplayResultAssertions.Same<CommitReceipt>(result, Result<object>.Succeed(expected));
        return result;
    }

    private static MutationReceipt ExpectedMutation(RemoteTransferDatabase fixture, Mutation mutation,
        CommitToken token, long? originalAcceptPosition) => mutation switch
        {
            PutDocument document => new("putDocument", document.Collection, document.Id, First),
            CreateQueueTransfer create => new(RemoteTransferProtocol.CreateReceiptKind, create.SourceQueue.Queue,
                create.TransferId.ToString(RemoteTransferProtocol.TransferIdFormat), First),
            AcceptQueueTransfer accept => new(RemoteTransferProtocol.AcceptReceiptKind, accept.DestinationQueue.Queue,
                fixture.Database.Verify<RemoteTransferIntentClaims>(accept.IntentToken).TransferId.ToString(RemoteTransferProtocol.TransferIdFormat),
                originalAcceptPosition ?? token.Position),
            CompleteQueueTransfer complete => new(RemoteTransferProtocol.CompleteReceiptKind, complete.SourceQueue.Queue,
                complete.TransferId.ToString(RemoteTransferProtocol.TransferIdFormat), SecondValue),
            _ => throw new InvalidOperationException(RuntimeMissingRow)
        };

    internal static async Task ReplayAsync(RemoteTransferDatabase fixture, CommandRequest request, Result<object> original)
    {
        var image = RemoteTransferCapacityAuthorityStorage.Capture(fixture);
        var position = fixture.Store.Position;
        await ReplayResultAssertions.Same<CommitReceipt>(fixture.Apply(OperationKind.Batch, request), original);
        await RemoteTransferCapacityAuthorityStorage.ImageAsync(fixture, image, position);
    }
}
