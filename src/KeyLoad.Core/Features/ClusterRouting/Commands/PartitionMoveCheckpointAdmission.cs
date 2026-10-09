using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private PartitionMoveParentPhase AdmitMoveParentPhase(IAtomicTransaction transaction,
        PrincipalRecord principal, PartitionMoveCheckpointBody body, PartitionMoveParentHeader header,
        PartitionMoveJournalReceipt checkpointReceipt, DateTimeOffset evaluatedAt)
    {
        var original = body.OriginalPhase
            ?? throw Errors.Fail(ErrorCode.Validation, PartitionMoveProtocol.Invalid);
        RequireMoveParentInterruptedAbortAdmission(transaction, principal, body, header, original);
        var cleanupGeneration = RequireMoveParentCleanupGeneration(transaction, body, header, original);
        if (body.OriginalPhaseCommandId == Guid.Empty
            || body.OriginalPhaseCommandId == header.MoveId || body.OriginalPhaseCommandId == checkpointReceipt.CommandId
            || body.ExpectedGeneration != header.Generation || body.OriginalExpiresAt <= evaluatedAt
            || original.Version != PartitionMoveProtocol.Version || original.MoveId != header.MoveId
            || original.Partition != header.Partition || original.Stage == PartitionMovePeerStage.ControlCheckpoint
            || !Enum.IsDefined(original.Stage) || original.PageOrdinal < PartitionMoveProtocol.EmptyCount
            || !PartitionMoveControlValidation.SameSource(original.SourcePlacement, header.SourcePlacement)
            || !PhysicalOwnerEntryValidation.SameOwner(original.ControlOwner, header.ControlOwner)
            || !PhysicalOwnerEntryValidation.SameOwner(original.DestinationOwner, header.DestinationOwner)
            || body.ObservedOriginalResult is not null || body.OriginalDescriptor is not null || body.OriginalFence is not null
            || body.NextOriginalPhaseCommandId is not null || body.NextOriginalPhase is not null
            || body.NextOriginalAuthorization is not null || body.NextOriginalExpiresAt != default
            || body.OriginalCaptureWitness is not null || body.OriginalOutcomeWitness is not null
            || body.NextOriginalRequestNonce != Guid.Empty || body.NextOriginalCaptureReleaseNonce != Guid.Empty
            || (original.Stage == PartitionMovePeerStage.Capture) != (body.OriginalCaptureReleaseNonce != Guid.Empty)
            || original.Stage == PartitionMovePeerStage.Capture && body.OriginalCaptureReleaseNonce == body.OriginalRequestNonce
            || (!PartitionMoveGrantValidation.IsLocalControl(original.Stage)) != (body.OriginalRequestNonce != Guid.Empty))
        { throw Errors.Fail(ErrorCode.Conflict, PartitionMoveProtocol.Conflict); }
        if (PartitionMoveParentStorage.Phase(transaction, header.Partition, header.MoveId,
            body.OriginalPhaseCommandId, Limits.MaxBatchBytes) is not null)
        { throw Errors.Fail(ErrorCode.Conflict, PartitionMoveProtocol.Conflict); }
        var grant = RequireCheckpointOriginalGrant(transaction, principal, body, header);
        var receiver = grant?.ReceiverOwner ?? header.ControlOwner;
        return new(PartitionMoveProtocol.Version, header.MoveId, header.Partition, body.OriginalPhaseCommandId,
            original.Stage, original.PageOrdinal, PartitionMoveParentValidation.BodyDigest(original),
            original, body.OriginalAuthorization, null, null, null, checkpointReceipt, null, grant,
            receiver, body.OriginalExpiresAt, null, body.OriginalRequestNonce, null,
            PartitionMoveOriginalDispatchIdentity.Digest(body.OriginalPhaseCommandId, original, grant, body.OriginalExpiresAt),
            null, body.OriginalCaptureReleaseNonce, OriginalIssuancePolicyEpoch: principal.PolicyEpoch, CleanupGeneration: cleanupGeneration);
    }

    private PartitionMovePhaseGrant? RequireCheckpointOriginalGrant(IKeyValueView view,
        PrincipalRecord principal, PartitionMoveCheckpointBody body, PartitionMoveParentHeader header)
    {
        var original = body.OriginalPhase ?? throw Errors.Fail(ErrorCode.Validation, PartitionMoveProtocol.Invalid);
        if (PartitionMoveGrantValidation.IsLocalControl(original.Stage))
        {
            if (original.GrantId is not null || body.OriginalAuthorization is not null
                || original.Resources.IsDefault || !original.Resources.IsEmpty)
            { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
            return null;
        }
        var grantId = original.GrantId
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.MissingAuthority);
        var grant = PartitionMoveGrantStorage.Read(view, header.Partition, grantId, Limits.MaxBatchBytes)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        var authorization = body.OriginalAuthorization;
        if (grant.MoveId != header.MoveId || grant.Partition != header.Partition
            || grant.PhaseCommandId != body.OriginalPhaseCommandId || grant.Stage != original.Stage
            || grant.PageOrdinal != original.PageOrdinal || grant.BodyDigest != PartitionMoveParentValidation.BodyDigest(original)
            || grant.ExpiresAt != body.OriginalExpiresAt || grant.OperatorPrincipalId != principal.Id
            || grant.OperatorPolicyEpoch != principal.PolicyEpoch || grant.Settlement is not null
            || grant.AbortDisposition is not null || grant.RetireCancellationDisposition is not null || authorization is null || authorization.CommandId != grant.GrantId
            || authorization.AppliedPosition != grant.AdmissionPosition
            || authorization.ControlIntentDigest != grant.ControlIntentDigest
            || original.ControlIntentDigest != grant.ControlIntentDigest
            || !PhysicalOwnerEntryValidation.SameOwner(grant.ControlOwner, header.ControlOwner)
            || !PhysicalOwnerEntryValidation.SameOwner(authorization.PhysicalOwner, header.ControlOwner)
            || !NativeSerialization.Serialize(grant.Resources).AsSpan().SequenceEqual(NativeSerialization.Serialize(original.Resources)))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        return grant;
    }
}
