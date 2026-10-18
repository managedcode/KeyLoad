namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private StreamTraversalCursor NewStreamTraversal(PrincipalRecord principal, ResourceDefinition resource,
        ReadStreamRequest request, StreamHead head, DateTimeOffset now, long applied)
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
            request.Stream, principal.Id, principal.PolicyEpoch, resource.SchemaVersion,
            request.Direction, head.TailRevision, head.FirstAvailableRevision, applied, next,
            now + eventSourceCursorLifetime);
    }

    private StreamTraversalCursor ReadStreamTraversalCursor(ReadStreamRequest request, PrincipalRecord principal,
        ResourceDefinition resource, StreamHead head, DateTimeOffset now, long applied)
    {
        var cursor = Verify<StreamTraversalCursor>(request.Cursor!, Limits.MaxBatchBytes);
        if (cursor.Version != StreamTraversalProtocol.Version || cursor.Purpose != StreamTraversalProtocol.Purpose
            || cursor.Incarnation != Store.Identity.Incarnation || cursor.Stream != request.Stream
            || cursor.PrincipalId != principal.Id || cursor.PolicyEpoch != principal.PolicyEpoch
            || cursor.SchemaVersion != resource.SchemaVersion || cursor.Direction != request.Direction || cursor.ExpiresAt <= now
            || cursor.SnapshotCutPosition < StreamTraversalProtocol.EmptyRevision || cursor.SnapshotCutPosition > applied
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
