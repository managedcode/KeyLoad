using KeyLoad.Core.Messaging;

namespace KeyLoad.FunctionalTests.Messaging;

internal static class RemoteTransferAttemptColdTrial
{
    internal static async Task RunAsync(int ceiling = RemoteTransferAttemptColdProtocol.Ceiling)
    {
        using var fixture = new RemoteTransferDatabase(new()
        {
            MaxQueueTransferAcceptAttempts = ceiling
        },
            destinationPolicy: new()
            {
                MaxStoredMessages = RemoteTransferAttemptColdProtocol.OneMessage
            },
            timeProvider: new RemoteTransferDiscoveryClock());
        fixture.Commit(fixture.DestinationPartition, new EnqueueMessage(fixture.DestinationQueue.Queue,
            RemoteTransferAttemptColdProtocol.FillerMessage, RemoteTransferAttemptColdProtocol.Payload));
        var transfer = new CreateQueueTransfer(fixture.SourceQueue, Guid.NewGuid(), fixture.DestinationQueue,
            new(fixture.DestinationQueue.Queue, RemoteTransferAttemptColdProtocol.OriginalMessage, RemoteTransferAttemptColdProtocol.Payload));
        fixture.Commit(fixture.SourcePartition, transfer);
        var original = fixture.Database.InspectQueueTransfer(RemoteTransferDatabase.RootPrincipal, fixture.SourceQueue, transfer.TransferId)!;
        var hint = RemoteTransferPendingDiscovery.Read(fixture.Database, RemoteTransferDatabase.RootPrincipal, null,
            CancellationToken.None).Hint ?? throw new InvalidOperationException(RemoteTransferAttemptColdProtocol.Missing);
        var accept = new CommandRequest(RemoteTransferAttemptIdentity.AcceptId(hint, hint.AcceptGeneration),
            fixture.DestinationPartition, [new AcceptQueueTransfer(fixture.DestinationQueue, original.IntentToken)]);
        var sourceCapacity = fixture.SourceCapacity(fixture.SourceQueue);
        var targetCapacity = fixture.TargetCapacity(fixture.DestinationQueue);
        var failed = fixture.Apply(OperationKind.Batch, accept);
        await Assert.That(Errors.Code(failed.Problem).Value).IsEqualTo(ErrorCode.ResourceExhausted);

        await Assert.That(failed.Value).IsNull();
        await RemoteTransferCoordinationColdAssertions.EqualAsync(fixture.SourceCapacity(fixture.SourceQueue), sourceCapacity);
        await RemoteTransferCoordinationColdAssertions.EqualAsync(fixture.TargetCapacity(fixture.DestinationQueue), targetCapacity);
        var bytes = RemoteTransferAttemptCold.Outcome(fixture, accept.CommandId, fixture.DestinationPartition);
        var request = RemoteTransferAttemptCold.Request(fixture, transfer, original.IntentToken,
            accept.CommandId, RemoteTransferAttemptProtocol.FailureReadPurpose);
        var before = fixture.Store.Position;
        var unchanged = Assert.ThrowsExactly<KeyLoadException>(() => RemoteTransferAttemptCold.Read(fixture, request));
        await Assert.That(unchanged.Code).IsEqualTo(ErrorCode.RevisionConflict);
        await Assert.That(fixture.Store.Position).IsEqualTo(before);
        await RemoteTransferCoordinationColdAssertions.EqualAsync(fixture.Database.InspectQueueTransfer(
            RemoteTransferDatabase.RootPrincipal, fixture.SourceQueue, transfer.TransferId), original);
        await Assert.That(fixture.Database.InspectMessage(RemoteTransferDatabase.RootPrincipal,
            fixture.DestinationQueue, RemoteTransferAttemptColdProtocol.OriginalMessage)).IsNull();
        await Assert.That(RemoteTransferAttemptCold.State(fixture, transfer.TransferId).Generation)
            .IsEqualTo(RemoteTransferAttemptColdProtocol.FirstGeneration);
        await RemoteTransferAttemptCold.AckAsync(fixture, RemoteTransferAttemptColdProtocol.FillerMessage);
        var witness = RemoteTransferAttemptCold.Read(fixture, request).FailureWitness
            ?? throw new InvalidOperationException(RemoteTransferAttemptColdProtocol.Missing);
        await RemoteTransferAttemptColdRefusals.BeforeAdvanceAsync(fixture, transfer, original, request, witness);
        var claims = fixture.Database.VerifyRetained<RemoteTransferAcceptFailureClaims>(witness);
        var advance = new CommandRequest(RemoteTransferAttemptIdentity.AdvanceId(hint, hint.AcceptGeneration, claims.OutcomeDigest),
            fixture.SourcePartition, [new AdvanceQueueTransferAttempt(fixture.SourceQueue, transfer.TransferId, hint.AcceptGeneration, witness)]);
        if (ceiling == RemoteTransferAttemptColdProtocol.SingleAttemptCeiling)
        {
            await RemoteTransferAttemptCeilingContinuation.RunAsync(fixture, transfer, original, firstAccept: accept,
                failed, bytes, advance);
            return;
        }
        var advanced = fixture.Apply(OperationKind.Batch, advance).Get<CommitReceipt>();
        await RemoteTransferAttemptColdContinuation.RunAsync(fixture, transfer, original, hint, accept, failed,
            bytes, advance, advanced, claims);
    }
}
