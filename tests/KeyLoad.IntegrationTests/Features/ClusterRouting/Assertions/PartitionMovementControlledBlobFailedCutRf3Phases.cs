using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PartitionMovementControlledBlobFailedCutRf3Phases
{
    internal static Dictionary<Guid, PartitionMovePeerStage> Expected(PartitionControlCommandRecord command, bool source)
    {
        var original = command.OriginalOperation!;
        if (!source)
        { return new() { [command.EffectId] = PartitionMovePeerStage.ControlApplyCommand }; }
        return new()
        {
            [ControlledDocumentTechnicalIdentity.Derive(original, ControlledDocumentTechnicalIdentity.Admit)] = PartitionMovePeerStage.ControlAdmitCommand,
            [ControlledDocumentTechnicalIdentity.Derive(original, ControlledDocumentTechnicalIdentity.Authorize)] = PartitionMovePeerStage.ControlAuthorize,
            [ControlledDocumentTechnicalIdentity.Derive(original, ControlledDocumentTechnicalIdentity.GrantAcknowledgement)] = PartitionMovePeerStage.ControlAcknowledge,
            [ControlledDocumentTechnicalIdentity.Derive(original, ControlledDocumentTechnicalIdentity.CommandAcknowledgement)] = PartitionMovePeerStage.ControlAcknowledgeCommand,
            [ControlledDocumentTechnicalIdentity.Derive(original, ControlledDocumentTechnicalIdentity.Finalize)] = PartitionMovePeerStage.ControlFinalizeCommand
        };
    }

    internal static async Task RequireOutcomeAsync(PartitionMovementPublicParentRf3NativeCut cut,
        ReplicatedOperation operation, PartitionMovePhaseCommand phase, long appliedIndex, PartitionControlCommandRecord command)
    {
        var outcome = PartitionMovementCapturePointerRf3Fault.Read<StoredOutcome>(cut,
            KeySpace.PartitionOutcome(phase.Partition, operation.PrincipalId, operation.Id));
        await Assert.That(outcome.Fingerprint).IsEqualTo(NativeOperationFingerprint.Compute(operation));
        await Assert.That(outcome.Result.Error).IsNull();
        await Assert.That(outcome.BlobAuthority).IsNull();
        await Assert.That(outcome.ScopeKind).IsEqualTo(CommandOutcomeScopeKind.Partition);
        await SqlRf3Protocol.EqualAsync(phase.Partition, outcome.Partition);
        RequireLocator(cut, phase.Partition, operation.PrincipalId, operation.Id);
        var result = outcome.Result.Get<PartitionMovePhaseResult>();
        await Assert.That(result.Stage).IsEqualTo(phase.Stage);
        await Assert.That(result.MoveId).IsEqualTo(phase.MoveId);
        await Assert.That(result.Journal.CommandId).IsEqualTo(operation.Id);
        await Assert.That(result.Journal.AppliedPosition).IsEqualTo(appliedIndex);
        if (phase.Stage == PartitionMovePeerStage.ControlApplyCommand)
        {
            var effect = result.ControlledEffect ?? throw new InvalidOperationException(PartitionMoveProtocol.MissingAuthority);
            await Assert.That(phase.Body.Span.SequenceEqual(command.TargetBody.Span)).IsTrue();
            await Assert.That(effect.OriginalResult.Error).IsEqualTo(command.OriginalResult!.Error);
            await Assert.That(effect.BlobAuthority).IsNull();
            await Assert.That(effect.Receipt.Mutations.IsEmpty).IsTrue();
            await SqlRf3Protocol.EqualAsync(command.TargetEffect, effect.Receipt);
        }
    }

    internal static void RequireLocator(PartitionMovementPublicParentRf3NativeCut cut, PartitionRef partition,
        string principal, Guid commandId)
    {
        var key = CommandOutcomePartitionLocatorSerialization.ScopedKey(partition, principal, commandId);
        var actual = PartitionMovementCapturePointerRf3Fault.Rows(cut)[Convert.ToHexString(key)];
        var expected = CommandOutcomePartitionLocatorSerialization.ScopedValue(partition, principal, commandId);
        if (actual != Convert.ToHexString(expected))
        { throw new InvalidOperationException(PartitionMoveProtocol.MissingAuthority); }
    }

    internal static async Task RequireGrantAsync(PartitionMovementPublicParentRf3Seed seed,
        PartitionMovementPublicParentRf3NativeCut source, PartitionControlCommandRecord command, PartitionMovePhaseGrant grant)
    {
        await Assert.That(grant.Stage).IsEqualTo(PartitionMovePeerStage.ControlApplyCommand);
        await Assert.That(grant.MoveId).IsEqualTo(seed.FirstRequest.MoveId);
        await SqlRf3Protocol.EqualAsync(seed.Partition, grant.Partition);
        await Assert.That(grant.BodyDigest).IsEqualTo(Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(command.TargetBody.Span)));
        await SqlRf3Protocol.EqualAsync(command.ControlOwner, grant.ControlOwner);
        await Assert.That(grant.ReceiverOwner.Incarnation).IsEqualTo(command.Destination.Incarnation);
        await Assert.That(grant.ReceiverOwner.PhysicalShardId).IsEqualTo(command.Destination.PhysicalShardId);
        await Assert.That(grant.RetireCancellationDisposition).IsNull();
        var encodedIndex = PartitionMovementCapturePointerRf3Fault.Rows(source)[Convert.ToHexString(
            PartitionMoveGrantStorage.MoveKey(seed.Partition, seed.FirstRequest.MoveId, grant.GrantId))];
        await Assert.That(encodedIndex).IsEqualTo(Convert.ToHexString(NativeSerialization.Serialize(grant.GrantId)));
        RequireLocator(source, seed.Partition, command.Identity.PrincipalId, command.Identity.CommandId);
    }

}
