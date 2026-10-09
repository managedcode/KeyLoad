using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private PartitionMoveParentHeader CreateMoveParentHeader(IAtomicTransaction transaction,
        PrincipalRecord principal, PartitionMoveCheckpointBody body, PartitionMovePhaseCommand checkpoint)
    {
        var request = body.OriginalTransferRequest;
        if (PartitionMoveControlStorage.ReadHistory(transaction, request.Partition, request.MoveId, Limits.MaxBatchBytes) is not null)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
        if (request.MoveId != checkpoint.MoveId || request.Partition != checkpoint.Partition
            || request.Mode != PartitionMoveMode.Transfer || request.ExpectedPlacementRevision < PartitionMoveProtocol.EmptyCount
            || request.MoveId == Guid.Empty || request.DestinationPhysicalShardId == Guid.Empty
            || body.ExpectedGeneration != PartitionMoveProtocol.EmptyCount || body.Action != PartitionMoveCheckpointAction.Admit
            || body.OriginalPhase?.Stage != PartitionMovePeerStage.ControlPrepare)
        { throw Errors.Fail(ErrorCode.Validation, PartitionMoveProtocol.Invalid); }
        var prepare = NativeSerialization.Deserialize<PartitionMovePrepareBody>(body.OriginalPhase.Body.Span);
        if (!prepare.RequireParentCheckpoint || prepare.OperatorPrincipalId != principal.Id
            || !NativeSerialization.Serialize(prepare.Request).AsSpan().SequenceEqual(NativeSerialization.Serialize(request)))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        ValidatePartition(request.Partition);
        var directory = RequireMoveDirectory(transaction);
        var destination = RequireMoveDestination(directory, request);
        var placement = ResolveRegisteredPlacement(transaction, request.Partition, directory.ControlOwner);
        if (placement.Revision != request.ExpectedPlacementRevision
            || !PartitionMoveControlValidation.SameSource(placement, checkpoint.SourcePlacement)
            || !PhysicalOwnerEntryValidation.SameOwner(destination, checkpoint.DestinationOwner)
            || !PhysicalOwnerEntryValidation.SameOwner(directory.ControlOwner, checkpoint.ControlOwner))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        var source = directory.Owners.SingleOrDefault(owner => owner.Owner.PhysicalShardId == placement.PhysicalShardId)?.Owner
            ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority);
        if (source.Incarnation != placement.Incarnation
            || !source.VoterIds.SequenceEqual(placement.VoterIds, StringComparer.Ordinal)
            || destination.PhysicalShardId == source.PhysicalShardId || destination.Incarnation == source.Incarnation
            || destination.VoterIds.Intersect(source.VoterIds, StringComparer.Ordinal).Any())
        { throw Errors.Fail(ErrorCode.Validation, PartitionMoveProtocol.OwnerMismatch); }
        var parentKey = PartitionMoveParentKeys.Active(request.Partition);
        if (transaction.ReadOwnedValue(parentKey) is { } existingParent)
        {
            var activeMoveId = NativeSerialization.Deserialize<Guid>(existingParent);
            var activeHeader = PartitionMoveParentStorage.Header(transaction, request.Partition,
                activeMoveId, Limits.MaxBatchBytes)
                ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority);
            if (activeHeader.TerminalResult is null)
            { throw Errors.Fail(ErrorCode.Conflict, PartitionMoveProtocol.Conflict); }
            throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority);
        }
        var active = PartitionMoveControlStorage.Read(transaction, request.Partition, Limits.MaxBatchBytes);
        if (active is not null && active.Phase is not (PartitionMovePhase.Aborted or PartitionMovePhase.Retired))
        { throw Errors.Fail(ErrorCode.Conflict, PartitionMoveProtocol.Conflict); }
        var count = PartitionMoveParentStorage.Counter(transaction, PartitionMoveParentKeys.DatabaseTerminalCount(request.Partition));
        var activeCount = PartitionMoveParentStorage.Counter(transaction, PartitionMoveParentKeys.DatabaseActive(request.Partition));
        if (count + activeCount >= movementCheckpoints.MaxRetainedTerminalMovesPerDatabase)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMoveProtocol.Capacity); }
        PartitionMoveParentStorage.ChangeCounter(transaction, PartitionMoveParentKeys.PrincipalActive(principal.Id),
            PartitionMoveProtocol.SequenceStep, movementCheckpoints.MaxActiveMovesPerPrincipal);
        PartitionMoveParentStorage.ChangeCounter(transaction, PartitionMoveParentKeys.DatabaseActive(request.Partition),
            PartitionMoveProtocol.SequenceStep, movementCheckpoints.MaxActiveMovesPerDatabase);
        transaction.Put(parentKey, NativeSerialization.Serialize(request.MoveId));
        return new(PartitionMoveProtocol.Version, request.MoveId, request.Partition, request, principal.Id,
            principal.PolicyEpoch, directory.ControlOwner, placement, destination, PartitionMoveProtocol.EmptyCount, PartitionMoveProtocol.EmptyCount, PartitionMoveProtocol.EmptyCount, null, null, null,
            OriginalSourceOwner: source);
    }
}
