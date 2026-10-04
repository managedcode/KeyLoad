using KeyLoad.Core.Features.Messaging;

namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class RemoteTransferIntentValidationTests
{
    private const string WrongPurpose = "keyload.queue-transfer.intent.invalid";
    private const string WrongPrincipal = "other-persisted-principal";
    private const string WrongFingerprint = "wrong-fingerprint";
    private const string WrongSourcePartitionId = "wrong-source-partition";
    private const string WrongDestinationPartitionId = "wrong-destination-partition";

    [Test]
    public async Task DestinationRejectsEverySignedIntentScopeMismatchWithoutEnqueueOrReceipt()
    {
        using var fixture = new RemoteTransferDatabase();
        var transferId = Guid.NewGuid();
        fixture.Commit(fixture.SourcePartition, new CreateQueueTransfer(fixture.SourceQueue, transferId,
            fixture.DestinationQueue, new(fixture.DestinationQueue.Queue, "bound-message", "{\"source\":1}")));
        var intentView = fixture.Database.InspectQueueTransfer(RemoteTransferDatabase.RootPrincipal,
            fixture.SourceQueue, transferId)!;
        var claims = fixture.Database.Verify<RemoteTransferIntentClaims>(intentView.IntentToken);

        foreach (var token in InvalidTokens(fixture, claims, intentView.IntentToken))
        {
            var result = fixture.Apply(OperationKind.Batch,
                new CommandRequest(Guid.NewGuid(), fixture.DestinationPartition,
                    [new AcceptQueueTransfer(fixture.DestinationQueue, token)]));
            await Assert.That(result.Error).IsEqualTo(ErrorCode.TokenInvalidated);
            await Assert.That(fixture.Database.InspectQueueTransferReceipt(RemoteTransferDatabase.RootPrincipal,
                fixture.DestinationQueue, fixture.SourceQueue, transferId)).IsNull();
            await Assert.That(fixture.Database.InspectMessage(RemoteTransferDatabase.RootPrincipal,
                fixture.DestinationQueue, "bound-message")).IsNull();
        }

        await Assert.That(fixture.Database.InspectQueueTransfer(RemoteTransferDatabase.RootPrincipal,
            fixture.SourceQueue, transferId)!.State).IsEqualTo(QueueTransferState.OutputPending);
    }

    private static string[] InvalidTokens(RemoteTransferDatabase fixture, RemoteTransferIntentClaims claims,
        string validToken)
    {
        var wrongSource = new QueueLaneRef(new(RemoteTransferDatabase.TenantId,
            RemoteTransferDatabase.DatabaseId, "orders", WrongSourcePartitionId), "wrong-source");
        var wrongDestination = new QueueLaneRef(new(RemoteTransferDatabase.TenantId,
            RemoteTransferDatabase.DatabaseId, "orders", WrongDestinationPartitionId), "wrong-destination");
        return
        [
            fixture.Database.Sign(claims with { Purpose = WrongPurpose }),
            fixture.Database.Sign(claims with { Incarnation = Guid.NewGuid() }),
            fixture.Database.Sign(claims with { Source = wrongSource }),
            fixture.Database.Sign(claims with { TransferId = Guid.NewGuid() }),
            fixture.Database.Sign(claims with { Destination = wrongDestination }),
            fixture.Database.Sign(claims with { PrincipalId = WrongPrincipal }),
            fixture.Database.Sign(claims with { Fingerprint = WrongFingerprint }),
            fixture.Database.Sign(claims with { Message = claims.Message with { PayloadJson = "{\"source\":2}" } }),
            fixture.Database.Sign(claims with { Message = claims.Message with { Queue = wrongDestination.Queue } }),
            validToken[..^1] + "!"
        ];
    }
}
