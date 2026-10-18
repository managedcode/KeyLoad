using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const long DefaultPlacementRevision = 0;
    private const string PlacementAdminRequired = "Cluster administration is required.";

    /// <summary>Reads the committed placement witness through persisted cluster-administrator authority.</summary>
    /// <param name="principalId">The current persisted principal selected by the trusted request boundary.</param>
    /// <param name="partition">The complete atomic-partition identity.</param>
    /// <returns>The same-view committed physical-owner witness.</returns>
    public AtomicPartitionPlacementResolution ReadAtomicPartitionPlacement(string principalId, PartitionRef partition)
        => Store.Read(view => ReadAtomicPartitionPlacement(view, principalId, partition));

    internal AtomicPartitionPlacementResolution ReadAtomicPartitionPlacement(IKeyValueView view,
        string principalId, PartitionRef partition)
    {
        const string ReadAtomicPartitionPlacementDetailText = "The physical shard catalog is not initialized.";

        ArgumentNullException.ThrowIfNull(view);
        ValidatePartition(partition);
        var principal = Principal(view, principalId, Clock.GetUtcNow());
        if (!principal.ClusterAdministrator)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, PlacementAdminRequired);
        }

        var catalog = PhysicalShardCatalogRecordSerialization.Read(view)
            ?? throw Errors.Fail(ErrorCode.NotFound, ReadAtomicPartitionPlacementDetailText);
        PhysicalShardCatalogValidation.ValidateCatalog(catalog);
        return ResolveRegisteredPlacement(partition, catalog.DefaultShard);
    }

    internal static AtomicPartitionPlacementResolution ReadPlacementWitness(IKeyValueView view,
        PartitionRef partition)
    {
        const string ReadPlacementWitnessDetailText = "The physical shard catalog is not initialized.";

        ArgumentNullException.ThrowIfNull(view);
        ValidatePartition(partition);
        var catalog = PhysicalShardCatalogRecordSerialization.Read(view)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, ReadPlacementWitnessDetailText);
        PhysicalShardCatalogValidation.ValidateCatalog(catalog);
        return ResolveRegisteredPlacement(partition, catalog.DefaultShard);
    }

    private static AtomicPartitionPlacementResolution ResolvePlacement(PartitionRef partition,
        PhysicalShardRecord owner)
        => new(AtomicPartitionPlacementProtocol.CurrentVersion, partition, owner.PhysicalShardId,
            owner.Incarnation, owner.VoterIds, owner.PlacementEpoch, DefaultPlacementRevision,
            DefaultPlacementRevision, true);
}
