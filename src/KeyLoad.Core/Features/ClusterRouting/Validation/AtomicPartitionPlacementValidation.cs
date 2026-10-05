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
        if (encodedLength < 0 || encodedLength > AtomicPartitionPlacementProtocol.MaximumEncodedBytes)
        { throw Errors.Fail(ErrorCode.Validation, OversizedRequest); }
    }

    internal static void ValidateRequest(BindAtomicPartitionPlacementRequest? request)
    {
        if (request is null || request.Version != AtomicPartitionPlacementProtocol.CurrentVersion
            || request.ExpectedRevision < 0 || request.PhysicalShardId == Guid.Empty || request.Partition is null)
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
        if (directory is null || directory.Version != AtomicPartitionPlacementProtocol.CurrentVersion
            || directory.Revision <= 0 || directory.ExplicitAssignmentCount is < 1
            or > AtomicPartitionPlacementProtocol.MaximumExplicitAssignments
            || directory.Revision < directory.ExplicitAssignmentCount)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidDirectory);
        }
    }

    internal static void ValidateRow(AtomicPartitionPlacementV1? row, PartitionRef requestedPartition,
        PhysicalShardRecord defaultShard)
    {
        if (row is null || row.Version != AtomicPartitionPlacementProtocol.CurrentVersion
            || row.Partition is null || row.PhysicalShardId == Guid.Empty || row.Revision <= 0
            || row.Partition != requestedPartition || row.PhysicalShardId != defaultShard.PhysicalShardId
            || row.Incarnation != defaultShard.Incarnation || row.VoterIds.IsDefaultOrEmpty
            || !row.VoterIds.SequenceEqual(defaultShard.VoterIds, StringComparer.Ordinal)
            || row.PlacementEpoch != defaultShard.PlacementEpoch)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidRow);
        }

        DatabaseEngine.ValidatePartition(row.Partition);
        _ = AtomicPartitionPlacementSerialization.SerializeBounded(row);
    }
}
