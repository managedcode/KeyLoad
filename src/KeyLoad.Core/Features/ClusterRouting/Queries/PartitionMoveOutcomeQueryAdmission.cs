using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal void ValidatePartitionMovementOutcomeQuery(PartitionMovePeerEnvelope original,
        PartitionMoveJournalReceipt? authorization, Guid phaseCommandId, ReadExecutionBudget work)
    {
        ArgumentNullException.ThrowIfNull(work);
        work.Check();
        PartitionMovePeerEnvelopeValidation.RequireStructure(original, Limits.MaxBatchBytes);
        if (phaseCommandId == Guid.Empty)
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
            { RequireMoveOutcomeGrant(admitted, original, authorization, phaseCommandId, control, directory); }
            return true;
        });
        work.Check();
    }

    private void RequireMoveOutcomeGrant(IKeyValueView view, PartitionMovePeerEnvelope original,
        PartitionMoveJournalReceipt? authorization, Guid commandId, PartitionMoveControlRecord? control,
        PhysicalOwnerDirectoryV1 directory)
    {
        var presented = original.Grant
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.MissingAuthority);
        var grant = PartitionMoveGrantStorage.Read(view, original.Partition, presented.GrantId,
            Limits.MaxBatchBytes)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        var immutable = grant with { Settlement = null, AbortDisposition = null };
        if (control is null || authorization is null || presented.Settlement is not null
            || presented.AbortDisposition is not null || grant.PhaseCommandId != commandId
            || JsonData.Fingerprint(immutable) != JsonData.Fingerprint(presented)
            || authorization.CommandId != grant.GrantId || authorization.AppliedPosition != grant.AdmissionPosition
            || authorization.ControlIntentDigest != grant.ControlIntentDigest
            || !PhysicalOwnerEntryValidation.SameOwner(authorization.PhysicalOwner, grant.ControlOwner)
            || PartitionMoveIntentIdentity.Digest(control) != original.ControlIntentDigest)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        RequireOriginalMoveOutcomeReceiver(original, grant.ReceiverOwner);
        RequireMoveOutcomeDirectory(original, grant, directory);
        var principal = RequireMoveDispatchPrincipal(view, grant.OperatorPrincipalId, EvaluationClock.GetUtcNow());
        if (principal.PolicyEpoch != grant.OperatorPolicyEpoch || principal.Id != control.PrincipalId)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
    }

    private static void RequireMoveOutcomeDirectory(PartitionMovePeerEnvelope original,
        PartitionMovePhaseGrant grant, PhysicalOwnerDirectoryV1 directory)
    {
        if (!directory.Owners.Any(entry => PhysicalOwnerEntryValidation.SameOwner(entry.Owner, grant.ReceiverOwner))
            || !directory.Owners.Any(entry => PhysicalOwnerEntryValidation.SameOwner(entry.Owner, original.DestinationOwner))
            || !directory.Owners.Any(entry => entry.Owner.PhysicalShardId == original.SourcePlacement.PhysicalShardId
                && entry.Owner.Incarnation == original.SourcePlacement.Incarnation
                && entry.Owner.VoterIds.SequenceEqual(original.SourcePlacement.VoterIds, StringComparer.Ordinal)))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
    }
}
