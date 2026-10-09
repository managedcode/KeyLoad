using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private void RequireMoveReceiverEffectAdmission(IKeyValueView view, PrincipalRecord principal,
        Guid commandId, PartitionMovePhaseCommand phase, DateTimeOffset evaluatedAt)
    {
        RequireMoveRetireNotCancelled(view, commandId, phase);
        var admission = phase.ReceiverEffectAdmission;
        if (admission is null)
        {
            if (view.ReadOwnedValue(PartitionMoveParentKeys.Active(phase.Partition)) is not null
                || view.ReadOwnedValue(PartitionMoveReceiverIssuanceStorage.CountKey(phase.Partition, phase.MoveId)) is not null)
            { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
            return;
        }
        if (admission.Version != PartitionMoveProtocol.Version || !admission.OriginalGrant.RequireReceiverIssuance
            || admission.ReceiverWitness.OriginalPhaseCommandId != commandId
            || admission.SourceDispatchWitness.OriginalPhaseCommandId != commandId)
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMoveProtocol.MissingAuthority); }
        var original = new PartitionMovePeerEnvelope(phase.Version, phase.MoveId, phase.Partition, phase.ControlOwner,
            phase.SourcePlacement, phase.DestinationOwner, phase.ControlIntentDigest, phase.Stage, phase.PageOrdinal,
            admission.OriginalExpiresAt, admission.OriginalRequestNonce, phase.Body, admission.OriginalGrant);
        var originalEpoch = RequireMoveReceiverOriginalObservationEpoch(view, principal, original, commandId);
        if (principal.PolicyEpoch != originalEpoch)
        { throw Errors.Fail(ErrorCode.PermissionDenied, ChangedOutcomePrincipalPolicyMessage); }
        var catalog = PhysicalShardCatalogRecordSerialization.Read(view)
            ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority);
        PhysicalShardCatalogValidation.ValidateCatalog(catalog);
        PartitionMoveGrantValidation.Require(original, commandId, catalog.DefaultShard, evaluatedAt);
        PartitionMoveResourceBinding.Require(original);
        if (phase.GrantId != admission.OriginalGrant.GrantId
            || !NativeSerialization.Serialize(phase.Resources).AsSpan()
                .SequenceEqual(NativeSerialization.Serialize(admission.OriginalGrant.Resources)))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
    }
}
