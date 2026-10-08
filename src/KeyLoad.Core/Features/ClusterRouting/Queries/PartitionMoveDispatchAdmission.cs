using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal void ValidatePartitionMovementDispatch(PartitionMovePeerEnvelope original,
        PartitionMoveJournalReceipt? authorization, Guid phaseCommandId, ReadExecutionBudget work)
    {
        ArgumentNullException.ThrowIfNull(work);
        work.Check();
        PartitionMovePeerEnvelopeValidation.RequireStructure(original, Limits.MaxBatchBytes);
        if (phaseCommandId == Guid.Empty || original.ExpiresAt <= EvaluationClock.GetUtcNow())
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        Store.Read(view =>
        {
            var admitted = work.CreateView(view);
            var directory = RequireMoveDirectory(admitted);
            if (!PhysicalOwnerEntryValidation.SameOwner(directory.ControlOwner, original.ControlOwner))
            { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
            var control = PartitionMoveControlStorage.ReadHistory(admitted, original.Partition,
                original.MoveId, Limits.MaxBatchBytes);
            if (PartitionMoveGrantValidation.IsLocalControl(original.Stage))
            { RequireLocalMoveDispatch(admitted, original, authorization, control); }
            else
            { RequireGrantedMoveDispatch(admitted, original, authorization, phaseCommandId, control, directory); }
            return true;
        });
        work.Check();
    }

    private void RequireGrantedMoveDispatch(IKeyValueView view, PartitionMovePeerEnvelope original,
        PartitionMoveJournalReceipt? authorization, Guid commandId, PartitionMoveControlRecord? control, PhysicalOwnerDirectoryV1 directory)
    {
        var presented = original.Grant
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.MissingAuthority);
        var grant = PartitionMoveGrantStorage.Read(view, original.Partition, presented.GrantId,
            Limits.MaxBatchBytes)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        if (control is null || authorization is null
            || JsonData.Fingerprint(grant) != JsonData.Fingerprint(presented)
            || authorization.CommandId != grant.GrantId
            || authorization.AppliedPosition != grant.AdmissionPosition
            || authorization.ControlIntentDigest != grant.ControlIntentDigest
            || !PhysicalOwnerEntryValidation.SameOwner(authorization.PhysicalOwner, grant.ControlOwner)
            || PartitionMoveIntentIdentity.Digest(control) != original.ControlIntentDigest)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        if (!directory.Owners.Any(entry => PhysicalOwnerEntryValidation.SameOwner(entry.Owner, grant.ReceiverOwner))
            || !directory.Owners.Any(entry => PhysicalOwnerEntryValidation.SameOwner(entry.Owner, original.DestinationOwner))
            || !directory.Owners.Any(entry => entry.Owner.PhysicalShardId == original.SourcePlacement.PhysicalShardId
                && entry.Owner.Incarnation == original.SourcePlacement.Incarnation
                && entry.Owner.VoterIds.SequenceEqual(original.SourcePlacement.VoterIds, StringComparer.Ordinal)))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        var now = EvaluationClock.GetUtcNow();
        var principal = RequireMoveDispatchPrincipal(view, grant.OperatorPrincipalId, now);
        if (principal.PolicyEpoch != grant.OperatorPolicyEpoch || control.PrincipalId != principal.Id)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        PartitionMoveGrantValidation.Require(original, commandId, grant.ReceiverOwner, now);
        var phase = new PartitionMovePhaseCommand(original.Version, original.MoveId, original.Partition,
            original.ControlOwner, original.SourcePlacement, original.DestinationOwner,
            original.ControlIntentDigest, original.Stage, original.PageOrdinal, original.Body);
        RequireMovePhaseIdentity(phase, control);
        PartitionMoveGrantStageAdmission.Require(view, phase, control, Limits.MaxBatchBytes);
    }

    private PrincipalRecord RequireMoveDispatchPrincipal(IKeyValueView view, string principalId,
        DateTimeOffset now)
    {
        var principal = Principal(view, principalId, now);
        if (!principal.ClusterAdministrator)
        { throw Errors.Fail(ErrorCode.PermissionDenied, ClusterAdministrationRequiredMessage); }
        return principal;
    }
}
