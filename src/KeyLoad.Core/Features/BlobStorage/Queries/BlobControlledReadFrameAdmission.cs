using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private ControlledBlobReadFrame CaptureControlledBlobFrame(IKeyValueView view, PrincipalRecord principal,
        BlobRef blob, ControlledBlobReadPurpose purpose, ReplicatedOperation? original, StoredOutcome? outcome,
        ReadOnlyMemory<byte> nativeRequest, DateTimeOffset expiry, Guid queryId)
    {
        var now = EvaluationClock.GetUtcNow();
        if (queryId == Guid.Empty || expiry <= now || expiry > now + TimeSpan.FromSeconds(Limits.QueryDeadlineSeconds))
        { throw Errors.Fail(ErrorCode.TokenInvalidated, PartitionMoveProtocol.OwnerMismatch); }
        var directory = RequireMoveDirectory(view);
        if (directory.ControlOwner.Incarnation != Store.Identity.Incarnation)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        var publication = PartitionMovePublishedPlacementStorage.Read(view, blob.Partition)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        var control = PartitionMoveControlStorage.ReadHistory(view, blob.Partition, publication.MoveId,
            Limits.MaxBatchBytes) ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        if (control.Phase != PartitionMovePhase.Retired || control.PublishedPlacement is null
            || JsonData.Fingerprint(control.PublishedPlacement) != JsonData.Fingerprint(publication.Placement))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        var placement = ResolveRegisteredPlacement(view, blob.Partition, directory.ControlOwner);
        if (placement.PhysicalShardId != publication.Destination.PhysicalShardId
            || placement.Incarnation != publication.Destination.Incarnation
            || placement.PlacementEpoch != publication.Placement.PlacementEpoch
            || placement.Revision != publication.Placement.Revision)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        var resource = view.GetRecord<ResourceDefinition>(KeySpace.Resource(blob.Partition.TenantId,
            blob.Partition.DatabaseId, blob.Resource))
            ?? throw Errors.Fail(ErrorCode.NotFound, DatabaseEngineResourceIsNotConfiguredDetail);
        if (resource.Kind != ResourceKind.BlobStore || resource.TransactionDomainId != blob.Partition.TransactionDomainId)
        { throw Errors.Fail(ErrorCode.Validation, DatabaseEngineResourceHasADifferentKindDetail); }
        BlobQuotaOperations.ValidatePolicy(resource);
        var frame = new ControlledBlobReadFrame(PartitionMoveProtocol.Version, queryId, control,
            publication, principal, resource, directory.Revision, expiry, purpose, original, outcome, nativeRequest);
        _ = BlobControlledReadScope.Require(frame);
        return frame;
    }
}
