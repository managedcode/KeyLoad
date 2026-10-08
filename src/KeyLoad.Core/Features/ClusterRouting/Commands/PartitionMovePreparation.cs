using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private PartitionMoveControlRecord PreparePartitionMove(IAtomicTransaction transaction,
        PrincipalRecord principal, PartitionMoveRequest request, long position)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidatePartition(request.Partition);
        if (!principal.ClusterAdministrator)
        { throw Errors.Fail(ErrorCode.PermissionDenied, ClusterAdministrationRequiredMessage); }
        if (request.MoveId == Guid.Empty || request.DestinationPhysicalShardId == Guid.Empty
            || request.Mode != PartitionMoveMode.Transfer || position <= PartitionMoveProtocol.EmptyCount)
        { throw Errors.Fail(ErrorCode.Validation, PartitionMoveProtocol.Invalid); }
        var directory = RequireMoveDirectory(transaction);
        var destination = RequireMoveDestination(directory, request);
        var history = PartitionMoveControlStorage.ReadHistory(transaction, request.Partition,
            request.MoveId, Limits.MaxBatchBytes);
        if (history is not null)
        { return RequireExistingMove(history, principal, request, destination); }
        var placement = ResolveRegisteredPlacement(transaction, request.Partition, directory.ControlOwner);
        if (placement.Revision != request.ExpectedPlacementRevision)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        var source = directory.Owners.SingleOrDefault(owner =>
            owner.Owner.PhysicalShardId == placement.PhysicalShardId)?.Owner
            ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority);
        if (source.Incarnation != placement.Incarnation
            || !source.VoterIds.SequenceEqual(placement.VoterIds, StringComparer.Ordinal)
            || destination.PhysicalShardId == source.PhysicalShardId
            || destination.Incarnation == source.Incarnation
            || destination.VoterIds.Intersect(source.VoterIds, StringComparer.Ordinal).Any())
        { throw Errors.Fail(ErrorCode.Validation, PartitionMoveProtocol.OwnerMismatch); }
        var active = PartitionMoveControlStorage.Read(transaction, request.Partition, Limits.MaxBatchBytes);
        if (active is not null && active.Phase is not (PartitionMovePhase.Retired or PartitionMovePhase.Aborted))
        { throw Errors.Fail(ErrorCode.Conflict, PartitionMoveProtocol.Conflict); }
        var record = new PartitionMoveControlRecord(PartitionMoveProtocol.Version, request.MoveId,
            request.Partition, principal.Id, principal.PolicyEpoch, placement, destination,
            PartitionMovePhase.Prepared, PartitionMoveProtocol.EmptyCount, position, null, null, null);
        PartitionMoveGrantStorage.ChangeOutstanding(transaction, principal.Id, true, Limits.MaxBatchMutations);
        PartitionMoveGrantStorage.ChangeOutstanding(transaction,
            PartitionMoveGrantStorage.DatabaseKey(request.Partition.TenantId, request.Partition.DatabaseId),
            true, Limits.MaxBatchMutations);
        PartitionMoveControlStorage.Write(transaction, record, Limits.MaxBatchBytes);
        return record;
    }

    private PhysicalOwnerDirectoryV1 RequireMoveDirectory(IKeyValueView view)
    {
        var directory = PhysicalOwnerDirectorySerialization.Read(view)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        PhysicalOwnerDirectoryValidation.Validate(directory);
        var catalog = PhysicalShardCatalogRecordSerialization.Read(view)
            ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority);
        PhysicalShardCatalogValidation.ValidateCatalog(catalog);
        if (directory.ControlOwner.Incarnation != Store.Identity.Incarnation
            || !PhysicalOwnerEntryValidation.SameOwner(directory.ControlOwner, catalog.DefaultShard))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        return directory;
    }

    private static PhysicalShardRecord RequireMoveDestination(PhysicalOwnerDirectoryV1 directory,
        PartitionMoveRequest request)
        => directory.Owners.SingleOrDefault(owner =>
            owner.Owner.PhysicalShardId == request.DestinationPhysicalShardId)?.Owner
            ?? throw Errors.Fail(ErrorCode.NotFound, PartitionMoveProtocol.OwnerMismatch);

    private static PartitionMoveControlRecord RequireExistingMove(PartitionMoveControlRecord existing,
        PrincipalRecord principal, PartitionMoveRequest request, PhysicalShardRecord destination)
    {
        if (existing.MoveId != request.MoveId || existing.PrincipalId != principal.Id
            || existing.SourcePlacement.Revision != request.ExpectedPlacementRevision
            || !PhysicalOwnerEntryValidation.SameOwner(existing.DestinationOwner, destination))
        { throw Errors.Fail(ErrorCode.Conflict, PartitionMoveProtocol.Conflict); }
        if (existing.Phase == PartitionMovePhase.Aborted)
        { throw Errors.Fail(ErrorCode.Conflict, PartitionMoveProtocol.Conflict); }
        return existing;
    }
}
