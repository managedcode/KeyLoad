using KeyLoad.Core;
using KeyLoad.Core.Features.Messaging;

namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class RemoteTransferCoordinationColdTests
{
    private const string MessageId = "coordinator-native-original";
    private const string Payload = "{\"native\":true}";
    private const long NoRecords = 0;
    private const long OneRecord = 1;
    private const int InitialAttempts = 0;
    private const long InitialStateVersion = 1;
    private const long InitialReadySequence = 1;
    private const string EmptyHeaders = "{}";

    [Test]
    public async Task NativePendingDiscoveryDoesNotMutateAndColdCanonicalAcceptCompleteRetainOriginalReceipts()
    {
        using var fixture = new RemoteTransferDatabase();
        var transfer = new CreateQueueTransfer(fixture.SourceQueue, Guid.NewGuid(), fixture.DestinationQueue,
            new(fixture.DestinationQueue.Queue, MessageId, Payload));
        var created = fixture.Commit(fixture.SourcePartition, transfer);
        var original = fixture.Database.InspectQueueTransfer(RemoteTransferDatabase.RootPrincipal, fixture.SourceQueue, transfer.TransferId)!;
        var before = fixture.Store.Position;
        var page = RemoteTransferPendingDiscovery.Read(fixture.Database, RemoteTransferDatabase.RootPrincipal, null, CancellationToken.None);
        await Assert.That(fixture.Store.Position).IsEqualTo(before);
        var hint = page.Hint ?? throw new InvalidOperationException(RemoteTransferCoordinationProtocol.InvalidHint);
        await Assert.That(hint.SourceCut.Position).IsEqualTo(before);
        await Assert.That(hint.SourceCut.Incarnation).IsEqualTo(created.Token.Incarnation);
        await Assert.That(hint.IntentDigest).IsEqualTo(RemoteTransferCoordinationIdentity.IntentDigest(original.IntentToken));
        await Assert.That(fixture.TargetCapacity(fixture.DestinationQueue).StoredRecords).IsEqualTo(NoRecords);
        var acceptId = RemoteTransferCoordinationIdentity.CommandId(hint, RemoteTransferCoordinationProtocol.AcceptStage);
        fixture.Reopen();
        var coldPage = RemoteTransferPendingDiscovery.Read(fixture.Database, RemoteTransferDatabase.RootPrincipal, page.Cursor, CancellationToken.None);
        var coldHint = coldPage.Hint ?? throw new InvalidOperationException(RemoteTransferCoordinationProtocol.InvalidHint);
        await Assert.That(RemoteTransferCoordinationIdentity.CommandId(coldHint, RemoteTransferCoordinationProtocol.AcceptStage)).IsEqualTo(acceptId);
        var accept = new CommandRequest(acceptId, fixture.DestinationPartition, [new AcceptQueueTransfer(fixture.DestinationQueue, original.IntentToken)]);
        var accepted = fixture.Apply(OperationKind.Batch, accept).Get<CommitReceipt>();
        var proof = fixture.Database.InspectQueueTransferReceipt(RemoteTransferDatabase.RootPrincipal,
            fixture.DestinationQueue, fixture.SourceQueue, transfer.TransferId)!;
        var complete = new CommandRequest(RemoteTransferCoordinationIdentity.CommandId(hint, RemoteTransferCoordinationProtocol.CompleteStage),
            fixture.SourcePartition, [new CompleteQueueTransfer(fixture.SourceQueue, transfer.TransferId, proof.ReceiptToken)]);
        var completed = fixture.Apply(OperationKind.Batch, complete).Get<CommitReceipt>();
        fixture.Reopen();
        await RemoteTransferCoordinationColdAssertions.EqualAsync(fixture.Apply(OperationKind.Batch, accept).Get<CommitReceipt>(), accepted);
        await RemoteTransferCoordinationColdAssertions.EqualAsync(fixture.Apply(OperationKind.Batch, complete).Get<CommitReceipt>(), completed);
        await RemoteTransferCoordinationColdAssertions.EqualAsync(fixture.Database.InspectQueueTransfer(RemoteTransferDatabase.RootPrincipal,
            fixture.SourceQueue, transfer.TransferId), original with { State = QueueTransferState.Delivered, ReceiptToken = proof.ReceiptToken });
        await RemoteTransferCoordinationColdAssertions.EqualAsync(fixture.Database.InspectQueueTransferReceipt(RemoteTransferDatabase.RootPrincipal,
            fixture.DestinationQueue, fixture.SourceQueue, transfer.TransferId), proof);
        await RemoteTransferCoordinationColdAssertions.EqualAsync(fixture.Database.InspectMessage(RemoteTransferDatabase.RootPrincipal,
            fixture.DestinationQueue, MessageId), new MessageInspection(new(MessageId, MessageState.Ready, InitialAttempts, InitialStateVersion, InitialReadySequence, null, null), Payload, EmptyHeaders));
        await Assert.That(fixture.SourceCapacity(fixture.SourceQueue).StoredRecords).IsEqualTo(OneRecord);
        await Assert.That(fixture.TargetCapacity(fixture.DestinationQueue).StoredRecords).IsEqualTo(OneRecord);
    }
}
