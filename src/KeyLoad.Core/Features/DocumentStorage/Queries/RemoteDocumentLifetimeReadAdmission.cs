using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const int DocumentLifetimeVersion = 2;

    internal RemoteDocumentReadFrameV2? CaptureRemoteDocumentReadV2(string principalId, EntityRef reference,
        DateTimeOffset expiry, ReadExecutionBudget work)
    {
        work.Check();
        var frame = Store.Read(view => CaptureRemoteDocumentLifetimeView(work.CreateView(view), principalId,
            reference, expiry, Guid.NewGuid()));
        work.CheckResult(frame);
        return frame;
    }

    internal void ValidateRemoteDocumentReadV2(RemoteDocumentReadFrameV2 original, ReadExecutionBudget work)
    {
        work.Check();
        var current = Store.Read(view => CaptureRemoteDocumentLifetimeView(work.CreateView(view), original.Principal.Id,
            original.Reference, original.ExpiresAt, original.QueryId));
        if (current is null || !NativeSerialization.Serialize(current).AsSpan().SequenceEqual(NativeSerialization.Serialize(original)))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, RemoteDocumentFenceChanged);
        }
        work.Check();
    }

    private RemoteDocumentReadFrameV2? CaptureRemoteDocumentLifetimeView(IKeyValueView view, string principalId,
        EntityRef reference, DateTimeOffset expiry, Guid queryId)
    {
        ValidatePartition(reference.Partition);
        JsonData.Identifier(reference.Collection);
        JsonData.Identifier(reference.Id);
        var now = EvaluationClock.GetUtcNow();
        var principal = Principal(view, principalId, now);
        if (!principal.ClusterAdministrator && principal.TenantId != reference.Partition.TenantId)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, RemoteDocumentTenantDenied);
        }
        Authorization.Require(principal, reference.Partition, reference.Collection, Capability.DocumentsRead);
        if (queryId == Guid.Empty || expiry <= now || expiry > now + TimeSpan.FromSeconds(Limits.QueryDeadlineSeconds))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, RemoteDocumentFenceChanged);
        }
        var catalog = PhysicalShardCatalogRecordSerialization.Read(view);
        if (catalog is null)
        {
            return null;
        }
        PhysicalShardCatalogValidation.ValidateCatalog(catalog);
        if (configuredPhysicalOwner is not null && !PhysicalOwnerEntryValidation.SameOwner(catalog.DefaultShard, configuredPhysicalOwner))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, RemoteDocumentFenceChanged);
        }
        var placement = ResolveRegisteredPlacement(view, reference.Partition, catalog.DefaultShard);
        if (placement.PhysicalShardId == catalog.DefaultShard.PhysicalShardId)
        {
            return null;
        }
        var directory = PhysicalOwnerDirectorySerialization.Read(view)
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, RemoteDocumentFenceChanged);
        var destination = directory.Owners.Single(entry => entry.Owner.PhysicalShardId == placement.PhysicalShardId);
        var resource = Resource(view, reference.Partition, reference.Collection, ResourceKind.Collection);
        var coverage = PartitionControlLifetimeCoverage.Capture(view, reference.Partition, [resource], Limits.MaxBatchBytes);
        var route = new RemoteDocumentReadFenceV1(principal.Id, principal.TenantId, principal.PolicyEpoch,
            placement, directory.Revision, destination);
        return new(DocumentLifetimeVersion, queryId, expiry, reference, route, principal, resource, coverage);
    }
}
