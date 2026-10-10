using KeyLoad.Core.Features.Messaging;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class RemoteTransferRepairColdAdvance
{
    internal static async Task<(CommandRequest Command, OperationResult Result)> ApplyAsync(RemoteTransferDatabase fixture,
        RemoteTransferCoordinationHint hint, string intent, QueueTransferRepairStage stage, string? receipt)
    {
        var request = RemoteTransferRepairColdNative.Request(hint, intent, stage, receipt);
        var read = RemoteTransferRepairColdNative.Read(fixture, request);
        await Assert.That(read.SourceState).IsNull();
        await Assert.That(read.FailureWitness).IsNull();
        await Assert.That(read.TargetReceipt).IsNull();
        var witness = read.RepairWitness ?? throw new InvalidOperationException(RemoteTransferRepairColdProtocol.Missing);
        var claims = fixture.Database.Verify<RemoteTransferRepairClaims>(witness, fixture.Database.Limits.MaxBatchBytes);
        await Assert.That(claims.OwnerCut.Position).IsEqualTo(claims.ReadGeneration);
        await Assert.That(claims.CurrentPolicyEpoch).IsEqualTo(RemoteTransferRepairColdNative.Principal(fixture).PolicyEpoch);
        var id = RemoteTransferRepairIdentity.AdvanceId(hint, stage, claims.OutcomeDigest);
        var command = new CommandRequest(id, hint.Source.Partition, [new AdvanceQueueTransferRepair(hint.Source,
            hint.TransferId, stage, hint.AcceptGeneration, hint.AcceptPolicyGeneration, hint.CompleteGeneration, witness)]);
        var before = fixture.Store.Position;
        var malformed = Assert.ThrowsExactly<KeyLoadException>(() => RemoteTransferRepairColdNative.Read(fixture,
            request with { AcceptCommandId = Guid.NewGuid() }));
        await Assert.That(malformed.Code).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(fixture.Store.Position).IsEqualTo(before);
        await RefuseMalformedWitnessAsync(fixture, command);
        var result = RemoteTransferRepairColdNative.Apply(fixture, command);
        if (result.Error is null)
        { await RefuseChangedAndStaleAsync(fixture, command); }
        return (command, result);
    }

    private static async Task RefuseMalformedWitnessAsync(RemoteTransferDatabase fixture, CommandRequest command)
    {
        var mutation = (AdvanceQueueTransferRepair)command.Mutations.Single();
        var state = RemoteTransferRepairColdNative.State(fixture, mutation.TransferId);
        var capacity = fixture.SourceCapacity(fixture.SourceQueue);
        var malformed = new CommandRequest(Guid.NewGuid(), command.Partition, [mutation with
            { FailureWitness = mutation.FailureWitness + RemoteTransferRepairColdProtocol.MalformedWitnessSuffix }]);
        await Assert.That(RemoteTransferRepairColdNative.Apply(fixture, malformed).Error).IsEqualTo(ErrorCode.TokenInvalidated);
        await RemoteTransferCoordinationColdAssertions.EqualAsync(RemoteTransferRepairColdNative.State(fixture, mutation.TransferId), state);
        await RemoteTransferCoordinationColdAssertions.EqualAsync(fixture.SourceCapacity(fixture.SourceQueue), capacity);
    }

    private static async Task RefuseChangedAndStaleAsync(RemoteTransferDatabase fixture, CommandRequest command)
    {
        var original = (AdvanceQueueTransferRepair)command.Mutations.Single();
        var state = RemoteTransferRepairColdNative.State(fixture, original.TransferId);
        var capacity = fixture.SourceCapacity(fixture.SourceQueue);
        var changed = command with
        {
            Mutations = [original with
            { Stage = original.Stage == QueueTransferRepairStage.Accept ? QueueTransferRepairStage.Complete : QueueTransferRepairStage.Accept }]
        };
        await Assert.That(RemoteTransferRepairColdNative.Apply(fixture, changed).Error).IsEqualTo(ErrorCode.Conflict);
        var stale = command with { CommandId = Guid.NewGuid() };
        await Assert.That(RemoteTransferRepairColdNative.Apply(fixture, stale).Error).IsEqualTo(ErrorCode.RevisionConflict);
        await RemoteTransferCoordinationColdAssertions.EqualAsync(RemoteTransferRepairColdNative.State(fixture, original.TransferId), state);
        await RemoteTransferCoordinationColdAssertions.EqualAsync(fixture.SourceCapacity(fixture.SourceQueue), capacity);
    }

    internal static async Task DeniedWithoutWitnessAsync(RemoteTransferDatabase fixture,
        RemoteTransferCoordinationHint hint, string intent, QueueTransferRepairStage stage, string? receipt)
    {
        var source = fixture.SourceCapacity(fixture.SourceQueue);
        var target = fixture.TargetCapacity(fixture.DestinationQueue);
        var before = fixture.Store.Position;
        var error = Assert.ThrowsExactly<KeyLoadException>(() => RemoteTransferRepairColdNative.Read(fixture,
            RemoteTransferRepairColdNative.Request(hint, intent, stage, receipt)));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(fixture.Store.Position).IsEqualTo(before);
        await RemoteTransferCoordinationColdAssertions.EqualAsync(fixture.SourceCapacity(fixture.SourceQueue), source);
        await RemoteTransferCoordinationColdAssertions.EqualAsync(fixture.TargetCapacity(fixture.DestinationQueue), target);
    }
}
