using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal PartitionMoveLifetimeProofV1 CaptureControlledCommandLifetimeProof(
        PartitionMovePeerEnvelope original, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        PartitionMovePeerEnvelopeValidation.RequireStructure(original, Limits.MaxBatchBytes);
        if (original.Stage != PartitionMovePeerStage.ControlApplyCommand || original.Grant is null)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.MissingAuthority);
        }
        var result = Store.Read(view => ReadControlledCommandLifetimeProof(view, original));
        token.ThrowIfCancellationRequested();
        return result;
    }

    private PartitionMoveLifetimeProofV1 ReadControlledCommandLifetimeProof(IKeyValueView view,
        PartitionMovePeerEnvelope original)
    {
        var grant = PartitionMoveGrantStorage.Read(view, original.Partition, original.Grant!.GrantId, Limits.MaxBatchBytes)
            ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority);
        var proof = PartitionControlLifetimeProofStorage.ReadGrant(view, grant, Limits.MaxBatchBytes);
        PartitionControlLifetimeProofValidation.RequireEnvelope(original, proof, Limits.MaxBatchBytes);
        var body = NativeSerialization.Deserialize<PartitionControlApplyBody>(original.Body.Span);
        var operatorPrincipal = RequireMoveDispatchPrincipal(view, grant.OperatorPrincipalId, EvaluationClock.GetUtcNow());
        var record = RequireControlledCommandRecord(view, operatorPrincipal, body.Delegation.Identity, body.Delegation.EffectId);
        var controlProof = PartitionControlLifetimeProofStorage.ReadCommand(view, record, Limits.MaxBatchBytes);
        if (!PartitionControlLifetimeProofValidation.Same(controlProof, proof.ControlProof)
            || !record.TargetBody.Span.SequenceEqual(original.Body.Span)
            || operatorPrincipal.PolicyEpoch != grant.OperatorPolicyEpoch
            || grant.AbortDisposition is not null || grant.RetireCancellationDisposition is not null)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch);
        }
        var directory = RequireMoveDirectory(view);
        RequireMoveOutcomeDirectory(original, grant, directory);
        _ = RequireRetiredCommandControl(view, operatorPrincipal, body.Control, out _);
        var principal = Principal(view, record.Identity.PrincipalId, EvaluationClock.GetUtcNow());
        if (!PartitionControlLifetimeProofValidation.Same(principal, body.Delegation.Principal))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch);
        }
        RequireControlledCommandPolicies(principal, record.OriginalOperation!, original.Partition, grant.Resources);
        PartitionControlLifetimeCoverage.RequireCurrent(view, controlProof.Coverage, grant.Resources, Limits.MaxBatchBytes);
        RequireControlledLifetimeIssuance(view, grant, proof, record);
        return proof;
    }

    private void RequireControlledLifetimeIssuance(IKeyValueView view, PartitionMovePhaseGrant grant,
        PartitionMoveLifetimeProofV1 proof, PartitionControlCommandRecord record)
    {
        var outcome = CommandOutcomeKeyResolver.Select(view, grant.OperatorPrincipalId, grant.GrantId,
            new CommandOutcomePartitionScope(CommandOutcomeScopeKind.Partition, grant.Partition)).Outcome;
        if (outcome is null || outcome.Fingerprint != proof.AuthorizationFingerprint || outcome.Result.Error is not null
            || outcome.Incarnation != Store.Identity.Incarnation || outcome.PolicyEpoch != grant.OperatorPolicyEpoch)
        {
            throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority);
        }
        var issued = outcome.Result.Get<PartitionMovePhaseResult>();
        if (issued.Stage != PartitionMovePeerStage.ControlAuthorize || issued.MoveId != grant.MoveId || issued.Grant is null
            || !PartitionControlLifetimeProofValidation.Same(issued.Grant, proof.OriginalGrant)
            || issued.Journal.CommandId != grant.GrantId || issued.Journal.AppliedPosition != grant.AdmissionPosition
            || issued.Journal.ControlIntentDigest != grant.ControlIntentDigest
            || !PhysicalOwnerEntryValidation.SameOwner(issued.Journal.PhysicalOwner, grant.ControlOwner))
        {
            throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority);
        }
        PartitionControlLifetimeFence.Require(view, grant, record);
    }
}
