using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.GraphTraversal.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private static PhysicalOwnerDirectoryV1 ReadRemoteGraphControlDirectory(IKeyValueView view,
        PhysicalShardRecord configuredControl)
    {
        ArgumentNullException.ThrowIfNull(configuredControl);
        var catalog = PhysicalShardCatalogRecordSerialization.Read(view)
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, RemoteGraphProtocol.MissingAuthority);
        PhysicalShardCatalogValidation.ValidateCatalog(catalog);
        var directory = PhysicalOwnerDirectorySerialization.Read(view)
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, RemoteGraphProtocol.MissingAuthority);
        PhysicalOwnerDirectoryValidation.Validate(directory);
        if (!PhysicalOwnerEntryValidation.SameOwner(catalog.DefaultShard, configuredControl)
            || !PhysicalOwnerEntryValidation.SameOwner(directory.ControlOwner, configuredControl))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, RemoteGraphProtocol.MissingAuthority);
        }
        return directory;
    }

    private static RegisteredPhysicalOwnerV1 RouteRemoteGraphOwner(PhysicalOwnerDirectoryV1 directory,
        AtomicPartitionPlacementResolution placement)
    {
        var owner = directory.Owners.SingleOrDefault(value =>
            value.Owner.PhysicalShardId == placement.PhysicalShardId);
        if (owner is null || !PhysicalOwnerEntryValidation.Valid(owner)
            || owner.Owner.Incarnation != placement.Incarnation
            || !owner.Owner.VoterIds.SequenceEqual(placement.VoterIds, StringComparer.Ordinal))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, RemoteGraphProtocol.MissingAuthority);
        }
        return owner;
    }
}
