using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal const string FollowerReadInvalid = "The follower document request is invalid.";
    internal const string FollowerReadOwnerChanged = "The selected follower owner changed before authorization.";
    internal const string FollowerReadLagExceeded = "The selected follower cut exceeds the declared position lag.";
    internal const string FollowerReadMinimumUnavailable = "The selected follower cut does not satisfy the acknowledged minimum.";
    internal const string FollowerReadReplicaUnavailable = "The selected replica is not a current follower.";
    private const int FollowerReadVersion = 1;
    private const long FollowerReadInitialPosition = 0;

    internal static void ValidateFollowerReadRequest(ReadFollowerDocumentRequestV1 request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Reference);
        if (request.Version != FollowerReadVersion || string.IsNullOrWhiteSpace(request.ReplicaId)
            || request.MaximumLagPositions < FollowerReadInitialPosition)
        { throw Errors.Fail(ErrorCode.Validation, FollowerReadInvalid); }
        ValidatePartition(request.Reference.Partition);
    }

    private static CommitToken FollowerReadToken(IKeyValueView view, PartitionRef partition, PhysicalShardRecord expectedOwner)
    {
        var placement = ReadPlacementWitness(view, partition);
        if (placement.PhysicalShardId != expectedOwner.PhysicalShardId
            || placement.Incarnation != expectedOwner.Incarnation || placement.PlacementEpoch != expectedOwner.PlacementEpoch
            || !placement.VoterIds.SequenceEqual(expectedOwner.VoterIds, StringComparer.Ordinal))
        { throw Errors.Fail(ErrorCode.OwnershipLost, FollowerReadOwnerChanged); }
        var bytes = view.ReadOwnedValue(KeySpace.AppliedBytes)
            ?? throw Errors.Fail(ErrorCode.Corruption, DocumentTokenCorruptPosition);
        var applied = NativeSerialization.Deserialize<long>(bytes);
        if (applied <= FollowerReadInitialPosition)
        { throw Errors.Fail(ErrorCode.Corruption, DocumentTokenCorruptPosition); }
        return new(placement.Incarnation, partition.AtomicPartitionId, applied, placement.PlacementEpoch);
    }

    private static long ValidateFollowerDataCut(ReadFollowerDocumentRequestV1 request, FollowerDocumentSnapshot snapshot,
        CommitToken authority, Guid nodeId, long generation)
    {
        if (snapshot.NodeId != nodeId || snapshot.ReadGeneration != generation
            || snapshot.ReplicaId != request.ReplicaId || snapshot.Token.Incarnation != authority.Incarnation
            || snapshot.Token.AtomicPartitionId != authority.AtomicPartitionId
            || snapshot.Token.OwnershipEpoch != authority.OwnershipEpoch || snapshot.Token.Position > authority.Position)
        { throw Errors.Fail(ErrorCode.OwnershipLost, FollowerReadOwnerChanged); }
        if (request.MinimumToken is { } minimum && snapshot.Token.Position < minimum.Position)
        { throw Errors.Fail(ErrorCode.HistoryUnavailable, FollowerReadMinimumUnavailable); }
        var lag = authority.Position - snapshot.Token.Position;
        if (lag > request.MaximumLagPositions)
        { throw Errors.Fail(ErrorCode.HistoryUnavailable, FollowerReadLagExceeded); }
        return lag;
    }
}
