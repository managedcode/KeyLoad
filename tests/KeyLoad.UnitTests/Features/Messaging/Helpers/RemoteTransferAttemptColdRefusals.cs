using KeyLoad.Core.Features.Messaging;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class RemoteTransferAttemptColdRefusals
{
    private const string InvalidNativePrefix = "invalid-";

    internal static async Task BeforeAdvanceAsync(RemoteTransferDatabase fixture, CreateQueueTransfer transfer,
        QueueTransferInspection original, RemoteTransferCoordinationReadRequest request, string witness)
    {
        var state = RemoteTransferAttemptColdNative.State(fixture, transfer.TransferId);
        var source = fixture.SourceCapacity(fixture.SourceQueue);
        var target = fixture.TargetCapacity(fixture.DestinationQueue);
        var unknown = request with { AcceptCommandId = Guid.NewGuid() };
        var position = fixture.Store.Position;
        var missing = Assert.ThrowsExactly<KeyLoadException>(() => RemoteTransferAttemptColdNative.Read(fixture, unknown));
        await Assert.That(missing.Code).IsEqualTo(ErrorCode.RecoveryRequired);
        await Assert.That(fixture.Store.Position).IsEqualTo(position);
        var malformed = new CommandRequest(Guid.NewGuid(), fixture.SourcePartition,
            [new AdvanceQueueTransferAttempt(fixture.SourceQueue, transfer.TransferId,
                RemoteTransferAttemptColdProtocol.FirstGeneration, InvalidNativePrefix + witness)]);
        await Assert.That(fixture.Apply(OperationKind.Batch, malformed).Error).IsEqualTo(ErrorCode.TokenInvalidated);
        var stale = new CommandRequest(Guid.NewGuid(), fixture.SourcePartition,
            [new AdvanceQueueTransferAttempt(fixture.SourceQueue, transfer.TransferId,
                RemoteTransferAttemptColdProtocol.SecondGeneration, witness)]);
        await Assert.That(fixture.Apply(OperationKind.Batch, stale).Error).IsEqualTo(ErrorCode.RevisionConflict);
        await RemoteTransferCoordinationColdAssertions.EqualAsync(RemoteTransferAttemptColdNative.State(fixture, transfer.TransferId), state);
        await RemoteTransferCoordinationColdAssertions.EqualAsync(fixture.SourceCapacity(fixture.SourceQueue), source);
        await RemoteTransferCoordinationColdAssertions.EqualAsync(fixture.TargetCapacity(fixture.DestinationQueue), target);
        await RemoteTransferCoordinationColdAssertions.EqualAsync(fixture.Database.InspectQueueTransfer(
            RemoteTransferDatabase.RootPrincipal, fixture.SourceQueue, transfer.TransferId), original);
        await Assert.That(fixture.Database.InspectQueueTransferReceipt(RemoteTransferDatabase.RootPrincipal,
            fixture.DestinationQueue, fixture.SourceQueue, transfer.TransferId)).IsNull();
        await Assert.That(fixture.Database.InspectMessage(RemoteTransferDatabase.RootPrincipal,
            fixture.DestinationQueue, RemoteTransferAttemptColdProtocol.OriginalMessage)).IsNull();
    }
}
