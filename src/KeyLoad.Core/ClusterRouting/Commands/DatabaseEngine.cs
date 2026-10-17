using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string AdminRequired = "Cluster administration is required.";
    private const string PlacementRevisionConflict = "The atomic partition placement revision is stale.";
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
        const string BindPlacementDetailText = "The physical shard catalog is not initialized.";
        const int DirectoryRevisionValidationBoundary = 0;

        var catalog = PhysicalShardCatalogRecordSerialization.Read(transaction)
            ?? throw Errors.Fail(ErrorCode.NotFound, BindPlacementDetailText);
        PhysicalShardCatalogValidation.ValidateCatalog(catalog);
        var selectedOwner = ResolveRegisteredPlacementOwner(transaction, catalog.DefaultShard,
            request.PhysicalShardId, ErrorCode.UnsupportedCapability);

        var directory = AtomicPartitionPlacementSerialization.ReadDirectory(transaction);
        if (directory is not null)
        {
            AtomicPartitionPlacementValidation.ValidateDirectory(directory);
        }

        var revision = directory?.Revision ?? DirectoryRevisionValidationBoundary;
        if (request.ExpectedRevision != revision)
        {
            throw Errors.Fail(ErrorCode.Conflict, PlacementRevisionConflict);
        }

        var row = AtomicPartitionPlacementSerialization.ReadRow(transaction, request.Partition);
        selectedOwner = ResolveMovementPlacementOwner(transaction, request.Partition, selectedOwner, row);
        return row is null ? CreatePlacement(transaction, request, directory, selectedOwner)
            : ValidateExistingPlacement(row, request, directory, selectedOwner);
    }

    private static OperationResult CreatePlacement(IAtomicTransaction transaction,
        BindAtomicPartitionPlacementRequest request, AtomicPartitionPlacementDirectoryV1? directory,
        PhysicalShardRecord defaultShard)
    {
        const int DirectoryExplicitAssignmentCountValidationBoundary = 0;
        const int CountStep = 1;
        const int DirectoryRevisionValidationBoundary = 0;
        const int CurrentRevisionStep = 1;

        var count = directory?.ExplicitAssignmentCount ?? DirectoryExplicitAssignmentCountValidationBoundary;
        if (count >= AtomicPartitionPlacementProtocol.MaximumExplicitAssignments)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, PlacementCapacityExceeded);
        }

        var nextCount = checked(count + CountStep);
        var currentRevision = directory?.Revision ?? DirectoryRevisionValidationBoundary;
        if (currentRevision == long.MaxValue)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, PlacementRevisionExhausted);
        }

        var nextRevision = checked(currentRevision + CurrentRevisionStep);
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
        const string MissingPlacementDirectoryDetail = "A placement row exists without its directory header.";
        const string PlacementRevisionBeyondDirectoryDetail = "A placement row revision exceeds its directory revision.";

        if (directory is null)
        {
            throw Errors.Fail(ErrorCode.Corruption, MissingPlacementDirectoryDetail);
        }

        AtomicPartitionPlacementValidation.ValidateRow(row, request.Partition, defaultShard);
        if (row.Revision > directory.Revision)
        {
            throw Errors.Fail(ErrorCode.Corruption, PlacementRevisionBeyondDirectoryDetail);
        }

        return Result(true);
    }
}
