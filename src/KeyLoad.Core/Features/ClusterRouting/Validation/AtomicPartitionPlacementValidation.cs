using System.Diagnostics.CodeAnalysis;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;

namespace KeyLoad.Core.Features.ClusterRouting.Validation;

internal static class AtomicPartitionPlacementValidation
{
    private const string InvalidRequest = "The atomic partition placement request is invalid.";
    private const string InvalidDirectory = "The committed atomic partition placement directory is malformed.";
    private const string InvalidRow = "The committed atomic partition placement row is malformed.";
    private const string OversizedRequest = "The atomic partition placement request exceeds its encoded byte limit.";

    internal static void ValidateEncodedRequestLength(int encodedLength)
    {
        const int EncodedLengthValidationBoundary = 0;

        if (encodedLength < EncodedLengthValidationBoundary || encodedLength > AtomicPartitionPlacementProtocol.MaximumEncodedBytes)
        { throw Errors.Fail(ErrorCode.Validation, OversizedRequest); }
    }

    internal static void ValidateRequest(BindAtomicPartitionPlacementRequest? request)
    {
        const int ExpectedRevisionValidationBoundary = 0;

        if (request is null || request.Version != AtomicPartitionPlacementProtocol.CurrentVersion
            || request.ExpectedRevision < ExpectedRevisionValidationBoundary || request.PhysicalShardId == Guid.Empty || request.Partition is null)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidRequest);
        }

        DatabaseEngine.ValidatePartition(request.Partition);
        _ = AtomicPartitionPlacementSerialization.SerializeBounded(request);
    }

    internal static void ValidateReadRequest(AtomicPartitionPlacementReadRequest? request)
    {
        if (request is null || request.Version != AtomicPartitionPlacementProtocol.CurrentVersion
            || request.Partition is null)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidRequest);
        }

        DatabaseEngine.ValidatePartition(request.Partition);
        _ = AtomicPartitionPlacementSerialization.SerializeBounded(request);
    }

    internal static void ValidateDirectory(AtomicPartitionPlacementDirectoryV1? directory)
    {
        const int RevisionValidationBoundary = 0;
        const int ExplicitAssignmentCountFirstCount = 1;

        if (directory is null || directory.Version != AtomicPartitionPlacementProtocol.CurrentVersion
            || directory.Revision <= RevisionValidationBoundary || directory.ExplicitAssignmentCount is < ExplicitAssignmentCountFirstCount
            or > AtomicPartitionPlacementProtocol.MaximumExplicitAssignments
            || directory.Revision < directory.ExplicitAssignmentCount)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidDirectory);
        }
    }

    internal static void ValidateRowShape([NotNull] AtomicPartitionPlacementV1? row, PartitionRef requestedPartition)
    {
        const int RevisionValidationBoundary = 0;

        if (row is null || row.Version != AtomicPartitionPlacementProtocol.CurrentVersion
            || row.Partition is null || row.PhysicalShardId == Guid.Empty || row.Revision <= RevisionValidationBoundary
            || row.Partition != requestedPartition || row.VoterIds.IsDefaultOrEmpty)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidRow);
        }
    }

    internal static void ValidateRow(AtomicPartitionPlacementV1? row, PartitionRef requestedPartition,
        PhysicalShardRecord defaultShard)
    {
        ValidateRowShape(row, requestedPartition);
        if (row.PhysicalShardId != defaultShard.PhysicalShardId
            || row.Incarnation != defaultShard.Incarnation
            || !row.VoterIds.SequenceEqual(defaultShard.VoterIds, StringComparer.Ordinal)
            || row.PlacementEpoch != defaultShard.PlacementEpoch)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidRow);
        }

        DatabaseEngine.ValidatePartition(row.Partition);
        _ = AtomicPartitionPlacementSerialization.SerializeBounded(row);
    }
}
