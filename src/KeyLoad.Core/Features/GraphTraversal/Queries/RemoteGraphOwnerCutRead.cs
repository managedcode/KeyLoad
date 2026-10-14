using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.GraphTraversal.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private RemoteGraphOwnerCutV1 CaptureRemoteGraphOwnerCut(IKeyValueView view, PrincipalRecord principal,
        PartitionRef partition, PhysicalShardRecord expectedOwner, long schemaVersion)
    {
        RequireLocalResourceOwner(view, partition);
        var catalog = PhysicalShardCatalogRecordSerialization.Read(view)
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, RemoteGraphProtocol.MissingAuthority);
        PhysicalShardCatalogValidation.ValidateCatalog(catalog);
        if (!PhysicalOwnerEntryValidation.SameOwner(catalog.DefaultShard, expectedOwner))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, RemoteGraphProtocol.MissingAuthority);
        }
        var placement = ReadPlacementWitness(view, partition);
        long? applied = null;
        view.ReadValue(KeySpace.AppliedBytes, bytes => applied = NativeSerialization.Deserialize<long>(bytes));
        if (applied is null)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, RemoteGraphProtocol.MissingApplied);
        }
        var identity = Store.Identity;
        return new(catalog.DefaultShard, placement, identity.NodeId, identity.Incarnation,
            identity.ReadGeneration, Store.Position, applied.Value, principal.PolicyEpoch, schemaVersion);
    }
}
