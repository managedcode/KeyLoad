using KeyLoad.Core.Features.Messaging;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class RemoteTransferRepairColdTrial
{
    internal static async Task RunAsync(int ceiling)
    {
        using var fixture = new RemoteTransferDatabase(new() { MaxQueueTransferRepairAttempts = ceiling });
        fixture.AddPrincipal(new(RemoteTransferRepairColdProtocol.Subject, RemoteTransferDatabase.TenantId,
            [new(RemoteTransferRepairColdProtocol.Wildcard, RemoteTransferRepairColdProtocol.Wildcard, Capability.All)],
            [RemoteTransferRepairColdProtocol.Wildcard])
        { ClusterAdministrator = true, PolicyEpoch = RemoteTransferRepairColdProtocol.InitialEpoch });
        var transfer = new CreateQueueTransfer(fixture.SourceQueue, Guid.NewGuid(), fixture.DestinationQueue,
            new(fixture.DestinationQueue.Queue, RemoteTransferRepairColdProtocol.Message, RemoteTransferRepairColdProtocol.Payload));
        var create = new CommandRequest(Guid.NewGuid(), fixture.SourcePartition, [transfer]);
        _ = RemoteTransferRepairColdNative.Apply(fixture, create).Get<CommitReceipt>();
        var original = fixture.Database.InspectQueueTransfer(RemoteTransferRepairColdProtocol.Subject, fixture.SourceQueue, transfer.TransferId)!;
        var hint = RemoteTransferRepairColdNative.Hint(fixture);
        RemoteTransferRepairColdNative.Policy(fixture, fixture.DestinationQueue, allow: false);
        var accept = new CommandRequest(RemoteTransferRepairIdentity.CommandId(hint, QueueTransferRepairStage.Accept),
            fixture.DestinationPartition, [new AcceptQueueTransfer(fixture.DestinationQueue, original.IntentToken)]);
        var denied = RemoteTransferRepairColdNative.Apply(fixture, accept);
        await Assert.That(denied.Error).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(denied.Json).IsNull();
        await Assert.That(denied.NativeValue).IsNull();
        var bytes = RemoteTransferRepairColdNative.Outcome(fixture, accept.CommandId, fixture.DestinationPartition);
        await RemoteTransferRepairColdAdvance.DeniedWithoutWitnessAsync(fixture, hint, original.IntentToken,
            QueueTransferRepairStage.Accept, null);
        await Assert.That(fixture.Database.InspectMessage(RemoteTransferDatabase.RootPrincipal,
            fixture.DestinationQueue, RemoteTransferRepairColdProtocol.Message)).IsNull();
        RemoteTransferRepairColdNative.Policy(fixture, fixture.DestinationQueue, allow: true);
        var advanced = await RemoteTransferRepairColdAdvance.ApplyAsync(fixture, hint, original.IntentToken,
            QueueTransferRepairStage.Accept, null);
        var advanceReceipt = advanced.Result.Get<CommitReceipt>();
        await RemoteTransferCoordinationColdAssertions.EqualAsync(RemoteTransferRepairColdNative.Apply(fixture, advanced.Command).Get<CommitReceipt>(), advanceReceipt);
        hint = RemoteTransferRepairColdNative.Hint(fixture);
        await Assert.That(hint.AcceptPolicyGeneration).IsEqualTo(RemoteTransferRepairColdProtocol.RepairedGeneration);
        await Assert.That(RemoteTransferRepairColdNative.Outcome(fixture, accept.CommandId, fixture.DestinationPartition).AsSpan().SequenceEqual(bytes)).IsTrue();
        var repaired = new CommandRequest(RemoteTransferRepairIdentity.CommandId(hint, QueueTransferRepairStage.Accept),
            fixture.DestinationPartition, accept.Mutations);
        var accepted = RemoteTransferRepairColdNative.Apply(fixture, repaired).Get<CommitReceipt>();
        await RemoteTransferRepairColdContinuation.RunAsync(fixture, transfer, original, hint, accept, bytes,
            repaired, accepted, ceiling);
    }
}
