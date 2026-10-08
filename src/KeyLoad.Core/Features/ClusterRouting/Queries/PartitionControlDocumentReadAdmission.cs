using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal PartitionControlDocumentReadFrame? TryCaptureControlledDocumentRead(string principalId,
        EntityRef reference, CommitToken? minimumToken, DateTimeOffset requestExpiry, ReadExecutionBudget work)
    {
        work.Check();
        var frame = Store.Read<PartitionControlDocumentReadFrame?>(view =>
        {
            var charged = work.CreateView(view);
            var principal = Principal(charged, principalId, EvaluationClock.GetUtcNow());
            Authorization.Require(principal, reference.Partition, reference.Collection, Capability.DocumentsRead);
            if (PartitionMovePublishedPlacementStorage.Read(charged, reference.Partition) is null)
            { return null; }
            return CaptureControlledDocumentReadView(charged, principalId, reference, minimumToken,
                requestExpiry, Guid.NewGuid());
        });
        work.MeasureResult(frame);
        work.Check();
        return frame;
    }

    internal PartitionControlDocumentReadFrame CaptureControlledDocumentRead(string principalId,
        EntityRef reference, CommitToken? minimumToken, DateTimeOffset requestExpiry, ReadExecutionBudget work)
    {
        work.Check();
        var frame = Store.Read(view => CaptureControlledDocumentReadView(work.CreateView(view), principalId,
            reference, minimumToken, requestExpiry, Guid.NewGuid()));
        work.MeasureResult(frame);
        work.Check();
        return frame;
    }

    internal void ValidateControlledDocumentRead(PartitionControlDocumentReadFrame original, ReadExecutionBudget work)
    {
        work.Check();
        var current = Store.Read(view => CaptureControlledDocumentReadView(work.CreateView(view), original.Principal.Id,
            original.Reference, original.MinimumToken, original.ExpiresAt, original.QueryId));
        if (JsonData.Fingerprint(current) != JsonData.Fingerprint(original))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        work.Check();
    }

    private PartitionControlDocumentReadFrame CaptureControlledDocumentReadView(IKeyValueView view,
        string principalId, EntityRef reference, CommitToken? minimumToken, DateTimeOffset expiry, Guid queryId)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ValidatePartition(reference.Partition);
        JsonData.Identifier(reference.Collection);
        JsonData.Identifier(reference.Id);
        var now = EvaluationClock.GetUtcNow();
        if (expiry <= now || expiry > now + TimeSpan.FromSeconds(Limits.QueryDeadlineSeconds))
        { throw Errors.Fail(ErrorCode.TokenInvalidated, PartitionMoveProtocol.OwnerMismatch); }
        var directory = RequireMoveDirectory(view);
        if (directory.ControlOwner.Incarnation != Store.Identity.Incarnation)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        var principal = Principal(view, principalId, now);
        if (!principal.ClusterAdministrator && principal.TenantId != reference.Partition.TenantId)
        { throw Errors.Fail(ErrorCode.PermissionDenied, RemoteDocumentTenantDenied); }
        Authorization.Require(principal, reference.Partition, reference.Collection, Capability.DocumentsRead);
        var publication = PartitionMovePublishedPlacementStorage.Read(view, reference.Partition)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        var control = PartitionMoveControlStorage.ReadHistory(view, reference.Partition, publication.MoveId,
            Limits.MaxBatchBytes) ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        if (control.Phase != PartitionMovePhase.Retired || control.PublishedPlacement is null
            || JsonData.Fingerprint(control.PublishedPlacement) != JsonData.Fingerprint(publication.Placement))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        var placement = ReadPlacementWitness(view, reference.Partition);
        if (placement.PhysicalShardId != publication.Destination.PhysicalShardId
            || placement.Incarnation != publication.Destination.Incarnation
            || placement.PlacementEpoch != publication.Placement.PlacementEpoch)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        var resource = view.GetRecord<ResourceDefinition>(KeySpace.Resource(reference.Partition.TenantId,
            reference.Partition.DatabaseId, reference.Collection))
            ?? throw Errors.Fail(ErrorCode.NotFound, DatabaseEngineResourceIsNotConfiguredDetail);
        if (resource.Kind != ResourceKind.Collection || resource.TransactionDomainId != reference.Partition.TransactionDomainId)
        { throw Errors.Fail(ErrorCode.Validation, DatabaseEngineResourceHasADifferentKindDetail); }
        return new(PartitionMoveProtocol.Version, queryId, control, publication, principal, resource,
            reference, minimumToken, expiry, directory.Revision);
    }
}
