using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string RemoteQueryFenceInvalid = "The remote partition query authority changed.";

    internal RemoteDocumentReadFenceV1 CaptureRemotePartitionQuery(IKeyValueView view,
        PrincipalRecord principal, PartitionRef partition, string collection, ReadExecutionBudgetReadGrant grant)
    {
        if (!principal.ClusterAdministrator && principal.TenantId != partition.TenantId)
        { throw Errors.Fail(ErrorCode.PermissionDenied, RemoteQueryFenceInvalid); }
        Authorization.Require(principal, partition, collection, Capability.Query | Capability.DocumentsRead);
        var catalog = PhysicalShardCatalogRecordSerialization.Read(view, grant)
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, RemoteQueryFenceInvalid);
        PhysicalShardCatalogValidation.ValidateCatalog(catalog);
        if (configuredPhysicalOwner is not null
            && !PhysicalOwnerEntryValidation.SameOwner(catalog.DefaultShard, configuredPhysicalOwner))
        { throw Errors.Fail(ErrorCode.OwnershipLost, RemoteQueryFenceInvalid); }
        var resolution = ReadAtomicPartitionPlacementForAuthorizedRouting(view, partition, grant);
        var directory = PhysicalOwnerDirectorySerialization.Read(view, grant)
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, RemoteQueryFenceInvalid);
        var destination = directory.Owners.SingleOrDefault(entry => entry.Owner.PhysicalShardId == resolution.PhysicalShardId)
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, RemoteQueryFenceInvalid);
        if (destination.Owner.Incarnation != resolution.Incarnation
            || destination.Owner.PlacementEpoch != resolution.PlacementEpoch
            || !destination.Owner.VoterIds.SequenceEqual(resolution.VoterIds, StringComparer.Ordinal))
        { throw Errors.Fail(ErrorCode.OwnershipLost, RemoteQueryFenceInvalid); }
        return new(principal.Id, principal.TenantId, principal.PolicyEpoch, resolution, directory.Revision, destination);
    }
}
