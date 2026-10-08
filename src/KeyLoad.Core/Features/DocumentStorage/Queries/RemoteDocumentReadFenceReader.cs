using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string RemoteDocumentFenceChanged = "The remote document read authority changed.";
    private const string RemoteDocumentTenantDenied = "The document partition belongs to another tenant.";

    /// <summary>Captures a remote route and fresh source identity under one canonical cut.</summary>
    /// <param name="principalId">Authenticated source subject.</param>
    /// <param name="reference">Requested document scope.</param>
    /// <param name="cancellationToken">Original native read cancellation.</param>
    /// <returns>A remote fence, or null for ordinary local execution.</returns>
    public RemoteDocumentReadFenceV1? CaptureRemoteDocumentRead(string principalId,
        EntityRef reference, CancellationToken cancellationToken)
        => Store.Read<RemoteDocumentReadFenceV1?>(view =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var principal = Principal(view, principalId, Clock.GetUtcNow());
            if (!principal.ClusterAdministrator && principal.TenantId != reference.Partition.TenantId)
            { throw Errors.Fail(ErrorCode.PermissionDenied, RemoteDocumentTenantDenied); }
            Authorization.Require(principal, reference.Partition, reference.Collection, Capability.DocumentsRead);
            var catalog = PhysicalShardCatalogRecordSerialization.Read(view);
            if (catalog is null)
            { return null; }
            PhysicalShardCatalogValidation.ValidateCatalog(catalog);
            if (configuredPhysicalOwner is not null
                && !PhysicalOwnerEntryValidation.SameOwner(catalog.DefaultShard, configuredPhysicalOwner))
            { throw Errors.Fail(ErrorCode.OwnershipLost, RemoteDocumentFenceChanged); }
            var placement = ResolveRegisteredPlacement(view, reference.Partition, catalog.DefaultShard);
            if (placement.PhysicalShardId == catalog.DefaultShard.PhysicalShardId)
            { return null; }
            var directory = PhysicalOwnerDirectorySerialization.Read(view)
                ?? throw Errors.Fail(ErrorCode.OwnershipLost, RemoteDocumentFenceChanged);
            var destination = directory.Owners.Single(entry => entry.Owner.PhysicalShardId == placement.PhysicalShardId);
            cancellationToken.ThrowIfCancellationRequested();
            return new(principal.Id, principal.TenantId, principal.PolicyEpoch, placement, directory.Revision, destination);
        });

    /// <summary>Rejects changed source identity, map or directory after a receiving read completes.</summary>
    /// <param name="fence">Original captured source fence.</param>
    /// <param name="reference">Original document reference.</param>
    /// <param name="cancellationToken">Original native read cancellation.</param>
    public void ValidateRemoteDocumentRead(RemoteDocumentReadFenceV1 fence,
        EntityRef reference, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fence);
        var current = CaptureRemoteDocumentRead(fence.PrincipalId, reference, cancellationToken);
        if (current is null || !NativeSerialization.Serialize(current).AsSpan()
            .SequenceEqual(NativeSerialization.Serialize(fence)))
        { throw Errors.Fail(ErrorCode.OwnershipLost, RemoteDocumentFenceChanged); }
    }
}
