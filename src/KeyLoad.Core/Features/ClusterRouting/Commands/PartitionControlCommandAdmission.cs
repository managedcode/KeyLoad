using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Core.Features.ClusterRouting.Queries;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private PartitionControlCommandRecord AdmitControlledDocumentCommand(IAtomicTransaction transaction,
        PrincipalRecord operatorPrincipal, PartitionControlAdmitBody body, long position, DateTimeOffset now)
    {
        var control = RequireRetiredCommandControl(transaction, operatorPrincipal, body.Control, out var destination);
        var original = VerifyOperationAuthority(body.OriginalOperation);
        var command = RequireControlledCommandBody(original, control.Partition);
        var principal = Principal(transaction, original.PrincipalId, now);
        var resources = PartitionMoveResources.Capture(transaction, control.Partition, Limits);
        RequireControlledCommandPolicies(principal, original, control.Partition, resources);
        var scope = CommandOutcomePartitionIdentity.Resolve(original);
        var identity = new PartitionControlCommandIdentity(scope.Kind, scope.Partition, original.PrincipalId, original.Id);
        var previous = PartitionControlCommandStorage.Read(transaction, identity, Limits.MaxBatchBytes);
        var fingerprint = CommandFingerprint(original);
        if (previous is not null)
        {
            if (previous.Fingerprint != fingerprint || previous.EffectId != body.EffectId)
            { throw Errors.Fail(ErrorCode.Conflict, CommandContentConflictMessage); }
            return previous;
        }
        if (body.EffectId == Guid.Empty || body.EffectId == original.Id || body.ExpiresAt <= now)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        var selected = CommandOutcomeKeyResolver.Select(transaction, original.PrincipalId, original.Id, scope);
        if (selected.Outcome is not null)
        { throw Errors.Fail(ErrorCode.Conflict, PartitionMoveProtocol.Conflict); }
        var owner = RequireMoveDirectory(transaction).ControlOwner;
        var delegation = new PartitionControlDelegation(PartitionMoveProtocol.Version, identity, fingerprint,
            body.EffectId, control.MoveId, destination, principal, body.ExpiresAt, position,
            resources, owner, original.Kind, original.PayloadJson, original);
        var targetBody = new PartitionControlApplyBody(operatorPrincipal.Id, control, delegation, command);
        RequireEncodedControlBody(targetBody);
        var targetBytes = NativeSerialization.Serialize(targetBody);
        var admitted = new PartitionControlCommandRecord(PartitionMoveProtocol.Version, identity, fingerprint,
            owner, destination, body.EffectId, position, PartitionControlCommandPhase.Admitted,
            null, null, null, null, delegation, original, targetBytes);
        PartitionControlCommandStorage.Write(transaction, admitted, Limits.MaxBatchBytes);
        PartitionMoveGrantStorage.ChangeOutstanding(transaction, principal.Id, true, Limits.MaxBatchMutations);
        PartitionMoveGrantStorage.ChangeOutstanding(transaction,
            PartitionMoveGrantStorage.DatabaseKey(control.Partition.TenantId, control.Partition.DatabaseId),
            true, Limits.MaxBatchMutations);
        return admitted;
    }

    private void RequireEncodedControlBody(PartitionControlApplyBody body)
    {
        if (NativeSerialization.Measure(body) > Limits.MaxBatchBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMoveProtocol.Capacity); }
    }

    private PartitionMoveControlRecord RequireRetiredCommandControl(IKeyValueView view,
        PrincipalRecord operatorPrincipal, PartitionMoveControlRecord expected, out AtomicPartitionPlacementResolution placement)
    {
        if (!operatorPrincipal.ClusterAdministrator)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        var control = PartitionMoveControlStorage.ReadHistory(view, expected.Partition, expected.MoveId, Limits.MaxBatchBytes)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        if (control.PrincipalId != operatorPrincipal.Id
            || control.Phase != PartitionMovePhase.Retired || control.PublishedPlacement is null
            || JsonData.Fingerprint(control) != JsonData.Fingerprint(expected))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        var directory = RequireMoveDirectory(view);
        placement = ResolveRegisteredPlacement(view, control.Partition, directory.ControlOwner);
        if (placement.PhysicalShardId != control.DestinationOwner.PhysicalShardId
            || placement.Incarnation != control.DestinationOwner.Incarnation
            || placement.PlacementEpoch != control.PublishedPlacement.PlacementEpoch
            || placement.Revision != control.PublishedPlacement.Revision)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        return control;
    }
}
