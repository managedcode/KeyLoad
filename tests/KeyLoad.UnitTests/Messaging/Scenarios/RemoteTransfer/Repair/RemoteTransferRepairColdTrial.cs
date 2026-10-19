using KeyLoad.Core.Features.Messaging;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class RemoteTransferRepairColdTrial
{
    internal static async Task RunAsync(int ceiling)
    {
        using var fixture = new RemoteTransferDatabase(new()
        {
            MaxQueueTransferRepairAttempts = ceiling
        },
            timeProvider: new RemoteTransferDiscoveryClock());
        fixture.AddPrincipal(new(RemoteTransferRepairColdProtocol.Subject, RemoteTransferDatabase.TenantId,
            [new(RemoteTransferRepairColdProtocol.Wildcard, RemoteTransferRepairColdProtocol.Wildcard, Capability.All)],
            [RemoteTransferRepairColdProtocol.Wildcard])
        {
            ClusterAdministrator = true,
            PolicyEpoch = RemoteTransferRepairColdProtocol.InitialEpoch
        });
        var transfer = new CreateQueueTransfer(fixture.SourceQueue, Guid.NewGuid(), fixture.DestinationQueue,
            new(fixture.DestinationQueue.Queue, RemoteTransferRepairColdProtocol.Message, RemoteTransferRepairColdProtocol.Payload));
        var create = new CommandRequest(Guid.NewGuid(), fixture.SourcePartition, [transfer]);
        _ = RemoteTransferRepairCold.Apply(fixture, create).Get<CommitReceipt>();
        var original = fixture.Database.InspectQueueTransfer(RemoteTransferRepairColdProtocol.Subject, fixture.SourceQueue, transfer.TransferId)!;
        var hint = RemoteTransferRepairCold.Hint(fixture);
        RemoteTransferRepairCold.Policy(fixture, fixture.DestinationQueue, allow: false);
        var accept = new CommandRequest(RemoteTransferRepairIdentity.CommandId(hint, QueueTransferRepairStage.Accept),
            fixture.DestinationPartition, [new AcceptQueueTransfer(fixture.DestinationQueue, original.IntentToken)]);
        var denied = RemoteTransferRepairCold.Apply(fixture, accept);
        await Assert.That(Errors.Code(denied.Problem)).IsEqualTo(ErrorCode.PermissionDenied);

        await Assert.That(denied.Value).IsNull();
        var bytes = RemoteTransferRepairCold.Outcome(fixture, accept.CommandId, fixture.DestinationPartition);
        await RemoteTransferRepairColdAdvance.DeniedWithoutWitnessAsync(fixture, hint, original.IntentToken,
            QueueTransferRepairStage.Accept, null);
        await Assert.That(fixture.Database.InspectMessage(RemoteTransferDatabase.RootPrincipal,
            fixture.DestinationQueue, RemoteTransferRepairColdProtocol.Message)).IsNull();
        RemoteTransferRepairCold.Policy(fixture, fixture.DestinationQueue, allow: true);
        var advanced = await RemoteTransferRepairColdAdvance.ApplyAsync(fixture, hint, original.IntentToken,
            QueueTransferRepairStage.Accept, null);
        var advanceReceipt = advanced.Result.Get<CommitReceipt>();
        await RemoteTransferCoordinationColdAssertions.EqualAsync(RemoteTransferRepairCold.Apply(fixture, advanced.Command).Get<CommitReceipt>(), advanceReceipt);
        hint = RemoteTransferRepairCold.Hint(fixture);
        await Assert.That(hint.AcceptPolicyGeneration).IsEqualTo(RemoteTransferRepairColdProtocol.RepairedGeneration);
        await Assert.That(RemoteTransferRepairCold.Outcome(fixture, accept.CommandId, fixture.DestinationPartition).AsSpan().SequenceEqual(bytes)).IsTrue();
        var repaired = new CommandRequest(RemoteTransferRepairIdentity.CommandId(hint, QueueTransferRepairStage.Accept),
            fixture.DestinationPartition, accept.Mutations);
        var accepted = RemoteTransferRepairCold.Apply(fixture, repaired).Get<CommitReceipt>();
        await RemoteTransferRepairColdContinuation.RunAsync(fixture, transfer, original, hint, accept, bytes,
            repaired, accepted, ceiling);
    }
}
