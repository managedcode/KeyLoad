using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Core.Features.ClusterRouting.Validation;

internal static class PartitionControlLifetimeProofValidation
{
    internal static PartitionMovePhaseGrant Issued(PartitionMovePhaseGrant grant)
        => grant with
        {
            Settlement = null,
            AbortDisposition = null
        };

    internal static ResourceLifetimeCoverageV1 Require(PartitionMovePhaseCommand phase,
        PartitionMoveLifetimeProofV1 proof, int maximumBytes)
    {
        if (proof is null || proof.OriginalGrant is null || proof.ControlProof is null
            || proof.ControlProof.OriginalDelegation is null
            || !PartitionMoveSourceFenceValidation.ValidDigest(proof.AuthorizationFingerprint)
            || phase.Stage != PartitionMovePeerStage.ControlApplyCommand
            || proof.OriginalGrant.Stage != phase.Stage || proof.OriginalGrant.Partition != phase.Partition
            || proof.OriginalGrant.MoveId != phase.MoveId || proof.OriginalGrant.GrantId != phase.GrantId
            || proof.OriginalGrant.ControlIntentDigest != phase.ControlIntentDigest
            || !PhysicalOwnerEntryValidation.SameOwner(proof.OriginalGrant.ControlOwner, phase.ControlOwner)
            || !PhysicalOwnerEntryValidation.SameOwner(proof.OriginalGrant.ReceiverOwner, phase.DestinationOwner)
            || proof.OriginalGrant.PageOrdinal != phase.PageOrdinal
            || proof.OriginalGrant.Settlement is not null || proof.OriginalGrant.AbortDisposition is not null
            || proof.OriginalGrant.RetireCancellationDisposition is not null)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.MissingAuthority);
        }
        var body = NativeSerialization.Deserialize<PartitionControlApplyBody>(phase.Body.Span);
        if (!Same(proof.ControlProof.OriginalDelegation, body.Delegation)
            || proof.OriginalGrant.OperatorPrincipalId != body.OperatorPrincipalId
            || body.Delegation.ControlOwner is null
            || !PhysicalOwnerEntryValidation.SameOwner(proof.OriginalGrant.ControlOwner, body.Delegation.ControlOwner)
            || !Same(proof.OriginalGrant.Resources, phase.Resources)
            || !Same(body.Delegation.Resources, phase.Resources)
            || proof.OriginalGrant.PhaseCommandId != body.Delegation.EffectId
            || proof.OriginalGrant.BodyDigest != Convert.ToHexStringLower(
                System.Security.Cryptography.SHA256.HashData(phase.Body.Span)))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch);
        }
        var coverage = proof.ControlProof.Coverage;
        PartitionControlLifetimeCoverage.Require(coverage, phase.Partition, phase.Resources, maximumBytes);
        if (NativeSerialization.Measure(proof) > maximumBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMoveProtocol.Capacity);
        }
        return coverage;
    }

    internal static void RequireEnvelope(PartitionMovePeerEnvelope envelope,
        PartitionMoveLifetimeProofV1 proof, int maximumBytes)
    {
        if (proof is null || envelope.Grant is null || !Same(proof.OriginalGrant, Issued(envelope.Grant)))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch);
        }
        var phase = new PartitionMovePhaseCommand(envelope.Version, envelope.MoveId, envelope.Partition,
            envelope.ControlOwner, envelope.SourcePlacement, envelope.DestinationOwner, envelope.ControlIntentDigest,
            envelope.Stage, envelope.PageOrdinal, envelope.Body, envelope.Grant.GrantId, envelope.Grant.Resources);
        _ = Require(phase, proof, maximumBytes);
    }

    internal static bool Same<T>(T expected, T actual)
        => NativeSerialization.Serialize(expected).AsSpan().SequenceEqual(NativeSerialization.Serialize(actual));
}
