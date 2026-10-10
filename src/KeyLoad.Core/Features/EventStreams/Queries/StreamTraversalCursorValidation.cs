namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private StreamTraversalCursor NewStreamTraversal(PrincipalRecord principal, ResourceDefinition resource,
        ReadStreamRequest request, AtomicPartitionPlacementResolution placement, StreamHead head, DateTimeOffset now)
    {
        if (head.TailRevision < StreamTraversalProtocol.EmptyRevision
            || head.FirstAvailableRevision < StreamTraversalProtocol.FirstRevision
            || head.TailRevision != long.MaxValue && head.FirstAvailableRevision > head.TailRevision + StreamTraversalProtocol.FirstRevision)
        {
            throw Errors.Fail(ErrorCode.Corruption, StreamTraversalProtocol.InvalidRequest);
        }
        if (head.TailRevision == long.MaxValue || request.AfterRevision > head.TailRevision + StreamTraversalProtocol.FirstRevision)
        {
            throw Errors.Fail(ErrorCode.Validation, StreamTraversalProtocol.InvalidRequest);
        }
        var next = request.Direction == StreamReadDirection.Backward
            && request.AfterRevision == StreamTraversalProtocol.EmptyRevision
            ? head.TailRevision + StreamTraversalProtocol.FirstRevision : request.AfterRevision;
        if (request.Direction == StreamReadDirection.Backward && next < head.FirstAvailableRevision)
        {
            throw Errors.Fail(ErrorCode.HistoryUnavailable, StreamTraversalProtocol.History);
        }
        return new(StreamTraversalProtocol.Version, StreamTraversalProtocol.Purpose, Store.Identity.Incarnation,
            request.Stream, principal.Id, principal.PolicyEpoch, resource.SchemaVersion, placement.PlacementEpoch,
            request.Direction, head.TailRevision, head.FirstAvailableRevision, Store.Position, next,
            now + eventSourceCursorLifetime, placement.Revision, placement.DirectoryRevision);
    }

    private StreamTraversalCursor ReadStreamTraversalCursor(ReadStreamRequest request, PrincipalRecord principal,
        ResourceDefinition resource, AtomicPartitionPlacementResolution placement, StreamHead head, DateTimeOffset now)
    {
        var cursor = Verify<StreamTraversalCursor>(request.Cursor!, Limits.MaxBatchBytes);
        if (cursor.Version != StreamTraversalProtocol.Version || cursor.Purpose != StreamTraversalProtocol.Purpose
            || cursor.Incarnation != Store.Identity.Incarnation || cursor.Stream != request.Stream
            || cursor.PrincipalId != principal.Id || cursor.PolicyEpoch != principal.PolicyEpoch
            || cursor.SchemaVersion != resource.SchemaVersion || cursor.PlacementEpoch != placement.PlacementEpoch
            || cursor.LogicalPlacementRevision != placement.Revision || cursor.DirectoryFence != placement.DirectoryRevision
            || cursor.Direction != request.Direction || cursor.ExpiresAt <= now
            || cursor.SnapshotCutPosition < StreamTraversalProtocol.EmptyRevision || cursor.SnapshotCutPosition > Store.Position
            || cursor.CapturedTailRevision < StreamTraversalProtocol.EmptyRevision || cursor.CapturedTailRevision == long.MaxValue
            || cursor.CapturedFirstAvailableRevision < StreamTraversalProtocol.FirstRevision
            || cursor.CapturedFirstAvailableRevision > cursor.CapturedTailRevision + StreamTraversalProtocol.FirstRevision
            || cursor.NextExclusiveRevision < StreamTraversalProtocol.EmptyRevision
            || cursor.NextExclusiveRevision > cursor.CapturedTailRevision + StreamTraversalProtocol.FirstRevision
            || head.TailRevision < cursor.CapturedTailRevision)
        {
            throw Errors.Fail(ErrorCode.CursorExpired, StreamTraversalProtocol.Expired);
        }
        if (head.FirstAvailableRevision > cursor.CapturedFirstAvailableRevision)
        {
            throw Errors.Fail(ErrorCode.HistoryUnavailable, StreamTraversalProtocol.History);
        }
        return cursor;
    }
}
