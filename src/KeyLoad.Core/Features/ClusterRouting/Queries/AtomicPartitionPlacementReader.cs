using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const int FallbackRevisionEmptyCount = 0;

    private const string PlacementAdminRequired = "Cluster administration is required.";
    private const string OrphanPlacement = "An atomic partition placement row exists without its directory header.";

    /// <summary>Reads a committed placement witness through persisted cluster-administrator authority.</summary>
    /// <param name="principalId">The current persisted principal selected by the trusted request boundary.</param>
    /// <param name="request">The versioned full atomic-partition identity.</param>
    /// <returns>The same-view default or explicit physical-owner witness.</returns>
    public AtomicPartitionPlacementResolution ReadAtomicPartitionPlacement(string principalId,
        AtomicPartitionPlacementReadRequest request)
        => Store.Read(view => ReadAtomicPartitionPlacement(view, principalId, request));

    internal AtomicPartitionPlacementResolution ReadAtomicPartitionPlacement(IKeyValueView view,
        string principalId, AtomicPartitionPlacementReadRequest request)
    {
        const string ReadAtomicPartitionPlacementDetailText = "The physical shard catalog is not initialized.";

        ArgumentNullException.ThrowIfNull(view);
        AtomicPartitionPlacementValidation.ValidateReadRequest(request);
        var partition = request.Partition;
        ValidatePartition(partition);
        var principal = Principal(view, principalId, Clock.GetUtcNow());
        if (!principal.ClusterAdministrator)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, PlacementAdminRequired);
        }

        var catalog = PhysicalShardCatalogRecordSerialization.Read(view)
            ?? throw Errors.Fail(ErrorCode.NotFound, ReadAtomicPartitionPlacementDetailText);
        PhysicalShardCatalogValidation.ValidateCatalog(catalog);
        return ResolvePlacement(view, partition, catalog.DefaultShard);
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
        return ResolvePlacement(view, partition, catalog.DefaultShard);
    }

    private static AtomicPartitionPlacementResolution ResolvePlacement(IKeyValueView view,
        PartitionRef partition, PhysicalShardRecord defaultShard)
    {
        var directory = AtomicPartitionPlacementSerialization.ReadDirectory(view);
        var row = AtomicPartitionPlacementSerialization.ReadRow(view, partition);
        return ResolvePlacement(partition, defaultShard, directory, row);
    }

    private static AtomicPartitionPlacementResolution ResolvePlacement(PartitionRef partition,
        PhysicalShardRecord defaultShard, AtomicPartitionPlacementDirectoryV1? directory,
        AtomicPartitionPlacementV1? row)
    {
        const int DirectoryRevisionValidationBoundary = 0;

        if (directory is null && row is not null)
        {
            throw Errors.Fail(ErrorCode.Corruption, OrphanPlacement);
        }

        if (directory is not null)
        {
            AtomicPartitionPlacementValidation.ValidateDirectory(directory);
        }

        var directoryRevision = directory?.Revision ?? DirectoryRevisionValidationBoundary;
        return row is null ? Fallback(partition, defaultShard, directoryRevision)
            : Explicit(partition, row, directory!, defaultShard, directoryRevision);
    }

    private static AtomicPartitionPlacementResolution Fallback(PartitionRef partition,
        PhysicalShardRecord defaultShard, long directoryRevision)
        => new(AtomicPartitionPlacementProtocol.CurrentVersion, partition, defaultShard.PhysicalShardId,
            defaultShard.Incarnation, defaultShard.VoterIds, defaultShard.PlacementEpoch, directoryRevision, FallbackRevisionEmptyCount, true);

    private static AtomicPartitionPlacementResolution Explicit(PartitionRef partition,
        AtomicPartitionPlacementV1 row, AtomicPartitionPlacementDirectoryV1 directory,
        PhysicalShardRecord defaultShard, long directoryRevision)
    {
        const string ExplicitDetailText = "A placement row revision exceeds its directory revision.";

        AtomicPartitionPlacementValidation.ValidateRow(row, partition, defaultShard);
        if (row.Revision > directory.Revision)
        {
            throw Errors.Fail(ErrorCode.Corruption, ExplicitDetailText);
        }

        return new(AtomicPartitionPlacementProtocol.CurrentVersion, partition, row.PhysicalShardId,
            defaultShard.Incarnation, defaultShard.VoterIds, defaultShard.PlacementEpoch, directoryRevision,
            row.Revision, false);
    }
}
