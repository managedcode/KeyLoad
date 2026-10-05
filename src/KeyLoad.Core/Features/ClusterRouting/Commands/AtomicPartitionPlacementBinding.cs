using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string AdminRequired = "Cluster administration is required.";
    private const string PlacementRevisionConflict = "The atomic partition placement revision is stale.";
    private const string PlacementOwnerUnsupported = "Only the committed default physical shard is supported.";
    private const string PlacementCapacityExceeded = "The atomic partition placement directory is full.";
    private const string PlacementRevisionExhausted = "The atomic partition placement revision is exhausted.";

    internal static OperationResult ExecuteBindAtomicPartitionPlacement(IAtomicTransaction transaction,
        PrincipalRecord principal, BindAtomicPartitionPlacementRequest request)
    {
        AtomicPartitionPlacementValidation.ValidateRequest(request);
        if (!principal.ClusterAdministrator)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, AdminRequired);
        }

        return BindPlacement(transaction, request);
    }

    private static OperationResult BindPlacement(IAtomicTransaction transaction,
        BindAtomicPartitionPlacementRequest request)
    {
        var catalog = PhysicalShardCatalogRecordSerialization.Read(transaction)
            ?? throw Errors.Fail(ErrorCode.NotFound, "The physical shard catalog is not initialized.");
        PhysicalShardCatalogValidation.ValidateCatalog(catalog);
        if (request.PhysicalShardId != catalog.DefaultShard.PhysicalShardId)
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, PlacementOwnerUnsupported);
        }

        var directory = AtomicPartitionPlacementSerialization.ReadDirectory(transaction);
        if (directory is not null)
        {
            AtomicPartitionPlacementValidation.ValidateDirectory(directory);
        }

        var revision = directory?.Revision ?? 0;
        if (request.ExpectedRevision != revision)
        {
            throw Errors.Fail(ErrorCode.Conflict, PlacementRevisionConflict);
        }

        var row = AtomicPartitionPlacementSerialization.ReadRow(transaction, request.Partition);
        return row is null ? CreatePlacement(transaction, request, directory, catalog.DefaultShard)
            : ValidateExistingPlacement(row, request, directory, catalog.DefaultShard);
    }

    private static OperationResult CreatePlacement(IAtomicTransaction transaction,
        BindAtomicPartitionPlacementRequest request, AtomicPartitionPlacementDirectoryV1? directory,
        PhysicalShardRecord defaultShard)
    {
        var count = directory?.ExplicitAssignmentCount ?? 0;
        if (count >= AtomicPartitionPlacementProtocol.MaximumExplicitAssignments)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, PlacementCapacityExceeded);
        }

        var nextCount = checked(count + 1);
        var currentRevision = directory?.Revision ?? 0;
        if (currentRevision == long.MaxValue)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, PlacementRevisionExhausted);
        }

        var nextRevision = checked(currentRevision + 1);
        var row = new AtomicPartitionPlacementV1(AtomicPartitionPlacementProtocol.CurrentVersion,
            request.Partition, request.PhysicalShardId, AtomicPartitionPlacementProtocol.InitialRowRevision,
            defaultShard.Incarnation, defaultShard.VoterIds, defaultShard.PlacementEpoch);
        var nextDirectory = new AtomicPartitionPlacementDirectoryV1(
            AtomicPartitionPlacementProtocol.CurrentVersion, nextRevision, nextCount);
        var rowBytes = AtomicPartitionPlacementSerialization.SerializeBounded(row);
        var directoryBytes = AtomicPartitionPlacementSerialization.SerializeBounded(nextDirectory);
        transaction.Put(AtomicPartitionPlacementSerialization.RowKey(request.Partition), rowBytes);
        transaction.Put(AtomicPartitionPlacementSerialization.DirectoryKey(), directoryBytes);
        return Result(true);
    }

    private static OperationResult ValidateExistingPlacement(AtomicPartitionPlacementV1 row,
        BindAtomicPartitionPlacementRequest request, AtomicPartitionPlacementDirectoryV1? directory,
        PhysicalShardRecord defaultShard)
    {
        if (directory is null)
        {
            throw Errors.Fail(ErrorCode.Corruption, "A placement row exists without its directory header.");
        }

        AtomicPartitionPlacementValidation.ValidateRow(row, request.Partition, defaultShard);
        if (row.Revision > directory.Revision)
        {
            throw Errors.Fail(ErrorCode.Corruption, "A placement row revision exceeds its directory revision.");
        }

        return Result(true);
    }
}
